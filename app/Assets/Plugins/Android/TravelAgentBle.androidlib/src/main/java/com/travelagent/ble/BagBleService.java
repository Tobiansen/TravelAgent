package com.travelagent.ble;

import android.annotation.SuppressLint;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.bluetooth.*;
import android.bluetooth.le.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;
import org.json.JSONObject;
import java.util.*;

/** A started connectedDevice service owns GATT while the Unity activity is paused. */
@SuppressLint("MissingPermission")
public final class BagBleService extends Service {
    public static final UUID SERVICE = UUID.fromString("70a7e000-9a55-4b4e-8b7c-27c478e9d751");
    public static final UUID COMMAND = UUID.fromString("70a7e001-9a55-4b4e-8b7c-27c478e9d751");
    public static final UUID RESPONSE = UUID.fromString("70a7e002-9a55-4b4e-8b7c-27c478e9d751");
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");
    private static final String CHANNEL = "bag_connection";
    private static final int NOTIFICATION_ID = 2307;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ArrayDeque<byte[]> writes = new ArrayDeque<>();
    private final BleFrames frames = new BleFrames();
    private BluetoothAdapter adapter;
    private BluetoothLeScanner scanner;
    private BluetoothGatt gatt;
    private BluetoothGattCharacteristic commandCharacteristic;
    private boolean ready, writing, discovering, scanning, authenticationAttempted, ownerAuthenticated, liveMotionCapable, liveEventsCapable;
    private int mtu = 23, messageId;
    private long linkGeneration;
    private long lastSnapshotAt, lastMotionSequence, lastMotionElapsed = -1;
    private String selectedAddress = "", deviceId = "", bootId = "";
    private OwnerTokenStore tokens;

    @Override public void onCreate() {
        super.onCreate();
        BluetoothManager manager = (BluetoothManager)getSystemService(BLUETOOTH_SERVICE);
        adapter = manager == null ? null : manager.getAdapter();
        tokens = new OwnerTokenStore(this);
        IntentFilter filter = new IntentFilter(BluetoothDevice.ACTION_BOND_STATE_CHANGED);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(bondReceiver, filter, Context.RECEIVER_EXPORTED);
        else registerReceiver(bondReceiver, filter);
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        try {
            Notification notification = notification("Suitcase Bluetooth active");
            if (Build.VERSION.SDK_INT >= 29) startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
            else startForeground(NOTIFICATION_ID, notification);
            if (intent == null) { BleBridge.transport("Connection service restarted. Reconnect to read the board's current state.", false); stopSelf(); return START_NOT_STICKY; }
            String value = intent.getStringExtra("value");
            String action = intent.getAction();
            if ("disconnect".equals(action)) { closeConnection("Disconnected by owner"); stopSelf(); }
            else if ("scan".equals(action)) scan();
            else if ("connect".equals(action)) connect(value);
            else if ("send".equals(action)) send(value);
            else if ("claim".equals(action)) claim();
        } catch (Exception exception) {
            BleBridge.notice("Bluetooth operation failed: " + exception.getClass().getSimpleName() + ". Check permissions and retry.");
            closeConnection("Bluetooth operation stopped; board state is unconfirmed."); stopSelf();
        }
        return START_NOT_STICKY;
    }

    private Notification notification(String text) {
        NotificationManager manager = (NotificationManager)getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(new NotificationChannel(CHANNEL, "Suitcase connection", NotificationManager.IMPORTANCE_LOW));
        Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
        PendingIntent pending = launch == null ? null : PendingIntent.getActivity(this, 0, launch, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
        return builder.setSmallIcon(android.R.drawable.stat_sys_data_bluetooth).setContentTitle("TravelAgent")
                .setContentText(text).setContentIntent(pending).setOngoing(true).build();
    }

    private boolean available() {
        if (adapter == null || !adapter.isEnabled()) { BleBridge.notice("Bluetooth is off or unavailable. Enable Bluetooth in Android settings."); return false; }
        return true;
    }

    private void scan() {
        if (!available()) { stopSelf(); return; }
        stopScan(); scanner = adapter.getBluetoothLeScanner();
        if (scanner == null) { BleBridge.notice("Bluetooth scanning is unavailable."); stopSelf(); return; }
        ScanFilter filter = new ScanFilter.Builder().setServiceUuid(new ParcelUuid(SERVICE)).build();
        scanner.startScan(Collections.singletonList(filter), new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build(), scanCallback);
        scanning = true; BleBridge.notice("Scanning for TravelAgent-compatible BLE firmware (15 seconds).");
        main.postDelayed(() -> { stopScan(); BleBridge.notice("Scan complete. No board listed? Its firmware must advertise the TravelAgent service."); if (gatt == null && selectedAddress.isEmpty()) stopSelf(); }, 15000);
    }

    private void stopScan() {
        if (scanning && scanner != null) { try { scanner.stopScan(scanCallback); } catch (RuntimeException ignored) { } }
        scanning = false;
    }

    private final ScanCallback scanCallback = new ScanCallback() {
        @Override public void onScanResult(int callbackType, ScanResult result) {
            JSONObject envelope = new JSONObject(), device = new JSONObject();
            String name = result.getScanRecord() == null ? null : result.getScanRecord().getDeviceName();
            BleBridge.put(device, "id", result.getDevice().getAddress()); BleBridge.put(device, "name", name == null ? "Suitcase device" : name);
            BleBridge.put(device, "rssi", result.getRssi()); BleBridge.put(device, "simulated", false);
            BleBridge.put(envelope, "type", "device"); BleBridge.put(envelope, "device", device); BleBridge.enqueue(envelope);
        }
        @Override public void onScanFailed(int errorCode) { main.post(() -> { scanning = false; BleBridge.notice("BLE scan failed (Android code " + errorCode + "). Retry from the Device screen."); if (gatt == null) stopSelf(); }); }
    };

    private void connect(String address) {
        if (!available()) { stopSelf(); return; }
        if (address == null || !BluetoothAdapter.checkBluetoothAddress(address)) { BleBridge.notice("Choose a discovered device before connecting."); return; }
        closeConnection("Connecting"); stopScan(); selectedAddress = address;
        BluetoothDevice device = adapter.getRemoteDevice(address);
        BleBridge.transport("Pairing / connecting to selected board", false);
        long generation = ++linkGeneration;
        main.postDelayed(() -> { if (generation == linkGeneration && !ready) { closeConnection("Bluetooth connection timed out. Check the board's pairing window and retry."); stopSelf(); } }, 30000);
        if (device.getBondState() == BluetoothDevice.BOND_BONDED) openGatt(device);
        else if (!device.createBond()) { closeConnection("Android could not begin pairing. Open the board's physical pairing window and retry."); stopSelf(); }
    }

    private final BroadcastReceiver bondReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            BluetoothDevice device = Build.VERSION.SDK_INT >= 33 ? intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE, BluetoothDevice.class) : intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE);
            if (device == null || !device.getAddress().equals(selectedAddress)) return;
            int state = intent.getIntExtra(BluetoothDevice.EXTRA_BOND_STATE, BluetoothDevice.BOND_NONE);
            if (state == BluetoothDevice.BOND_BONDED && gatt == null) openGatt(device);
            else if (state == BluetoothDevice.BOND_NONE) { closeConnection("Pairing ended. Open the board's pairing window and retry."); stopSelf(); }
        }
    };

    private void openGatt(BluetoothDevice device) {
        gatt = device.connectGatt(this, false, callbacks, BluetoothDevice.TRANSPORT_LE);
        if (gatt == null) { closeConnection("Android could not create a Bluetooth connection."); stopSelf(); }
    }

    private final BluetoothGattCallback callbacks = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(BluetoothGatt source, int status, int state) { main.post(() -> {
            if (source != gatt) { source.close(); return; }
            if (status != BluetoothGatt.GATT_SUCCESS || state == BluetoothProfile.STATE_DISCONNECTED) {
                closeConnection("Bluetooth disconnected (Android code " + status + "). Board operation continues independently."); stopSelf(); return;
            }
            if (state == BluetoothProfile.STATE_CONNECTED) {
                if (!source.requestMtu(185)) discover(source);
                main.postDelayed(() -> { if (source == gatt && !discovering && !ready) discover(source); }, 2000);
            }
        }); }
        @Override public void onMtuChanged(BluetoothGatt source, int negotiatedMtu, int status) { main.post(() -> {
            if (source != gatt) return;
            mtu = status == BluetoothGatt.GATT_SUCCESS ? negotiatedMtu : 23;
            discover(source);
        }); }
        @Override public void onServicesDiscovered(BluetoothGatt source, int status) { main.post(() -> {
            if (source != gatt) return;
            BluetoothGattService service = source.getService(SERVICE);
            BluetoothGattCharacteristic response = service == null ? null : service.getCharacteristic(RESPONSE);
            commandCharacteristic = service == null ? null : service.getCharacteristic(COMMAND);
            if (status != BluetoothGatt.GATT_SUCCESS || commandCharacteristic == null || response == null) {
                closeConnection("The board does not expose the TravelAgent BLE protocol. Firmware update required."); stopSelf(); return;
            }
            BluetoothGattDescriptor descriptor = response.getDescriptor(CCCD);
            if (descriptor == null || !source.setCharacteristicNotification(response, true)) { closeConnection("Board notification setup failed."); stopSelf(); return; }
            boolean started;
            if (Build.VERSION.SDK_INT >= 33) started = source.writeDescriptor(descriptor, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE) == BluetoothStatusCodes.SUCCESS;
            else { descriptor.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE); started = source.writeDescriptor(descriptor); }
            if (!started) { closeConnection("Board notification subscription could not start."); stopSelf(); }
        }); }
        @Override public void onDescriptorWrite(BluetoothGatt source, BluetoothGattDescriptor descriptor, int status) { main.post(() -> {
            if (source != gatt || !CCCD.equals(descriptor.getUuid())) return;
            if (status != BluetoothGatt.GATT_SUCCESS) { closeConnection("Encrypted notifications were rejected (Android code " + status + "). Pair again."); stopSelf(); return; }
            ready = true; BleBridge.transport("BLE connected; waiting for authoritative board snapshot", true);
            internalCommand("getSnapshot", null);
        }); }
        @Override public void onCharacteristicWrite(BluetoothGatt source, BluetoothGattCharacteristic characteristic, int status) { main.post(() -> {
            if (source != gatt || !COMMAND.equals(characteristic.getUuid())) return;
            writing = false;
            if (status != BluetoothGatt.GATT_SUCCESS) { writes.clear(); BleBridge.notice("Bluetooth write failed (Android code " + status + "). Device execution is unconfirmed."); return; }
            if (!writes.isEmpty()) writes.removeFirst(); writeNext();
        }); }
        @Override public void onCharacteristicChanged(BluetoothGatt source, BluetoothGattCharacteristic characteristic) {
            byte[] value = characteristic.getValue(); if (value != null) receive(source, value.clone());
        }
        @Override public void onCharacteristicChanged(BluetoothGatt source, BluetoothGattCharacteristic characteristic, byte[] value) { receive(source, value.clone()); }
    };

    private void discover(BluetoothGatt source) {
        if (source != gatt || discovering || ready) return;
        discovering = true;
        if (!source.discoverServices()) { closeConnection("Bluetooth service discovery failed."); stopSelf(); }
    }

    private void send(String json) throws Exception {
        if (!ready || gatt == null) { BleBridge.notice("Bluetooth is not ready. Reconnect before sending a command."); return; }
        JSONObject envelope = new JSONObject(json);
        envelope.put("type", "command"); envelope.put("protocolVersion", 1); envelope.put("deviceId", deviceId); envelope.put("bootId", bootId);
        queue(envelope.toString());
    }

    private void queue(String json) {
        if (!ready || gatt == null) return;
        List<byte[]> chunks = BleFrames.split(json, mtu, ++messageId);
        if (writes.size() + chunks.size() > 1024) { BleBridge.notice("Bluetooth output queue is full. Wait and retry."); return; }
        writes.addAll(chunks); writeNext();
    }

    private void writeNext() {
        if (writing || writes.isEmpty() || gatt == null || commandCharacteristic == null || !ready) return;
        byte[] next = writes.peekFirst(); writing = true;
        boolean started;
        if (Build.VERSION.SDK_INT >= 33) started = gatt.writeCharacteristic(commandCharacteristic, next, BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT) == BluetoothStatusCodes.SUCCESS;
        else { commandCharacteristic.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT); commandCharacteristic.setValue(next); started = gatt.writeCharacteristic(commandCharacteristic); }
        if (!started) { writing = false; writes.clear(); BleBridge.notice("Bluetooth could not send the command. Execution is unconfirmed."); return; }
        long generation = linkGeneration;
        main.postDelayed(() -> { if (generation == linkGeneration && writing && writes.peekFirst() == next) {
            writing = false; writes.clear(); closeConnection("Bluetooth write timed out. Reconnect and read the board state before retrying."); stopSelf();
        } }, 5000);
    }

    private void receive(BluetoothGatt source, byte[] bytes) { main.post(() -> {
        if (source != gatt || !ready) return;
        try {
            String json = frames.accept(bytes, SystemClock.elapsedRealtime());
            if (json == null) return;
            JSONObject message = new JSONObject(json);
            if (message.optInt("protocolVersion", -1) != 1) { BleBridge.notice("Unsupported device protocol version."); return; }
            JSONObject snapshot = message.optJSONObject("snapshot");
            if (snapshot != null) {
                String nextDevice = snapshot.optString("deviceId", "");
                if (!deviceId.isEmpty() && !deviceId.equals(nextDevice)) { BleBridge.notice("Board identity changed during connection; reconnect to verify it."); return; }
                String nextBoot = snapshot.optString("bootId", "");
                if (!bootId.equals(nextBoot)) { lastMotionSequence = 0; lastMotionElapsed = -1; BleBridge.clearMotion(); }
                deviceId = nextDevice; bootId = nextBoot;
                ownerAuthenticated = snapshot.optBoolean("authenticated");
                liveMotionCapable = false; liveEventsCapable = false;
                org.json.JSONArray capabilities = snapshot.optJSONArray("capabilities");
                if (capabilities != null) for (int i = 0; i < capabilities.length(); i++) {
                    if ("liveMotion".equals(capabilities.optString(i))) liveMotionCapable = true;
                    if ("liveEvents".equals(capabilities.optString(i))) liveEventsCapable = true;
                }
                lastSnapshotAt = SystemClock.elapsedRealtime();
                if (!ownerAuthenticated) BleBridge.clearMotion();
            }
            if ("motion".equals(message.optString("type"))) {
                JSONObject sample = message.optJSONObject("sample");
                if (message.has("ownerToken") || sample == null || !(sample.opt("sensorFresh") instanceof Boolean) ||
                    !integer(sample.opt("protocolVersion")) || !integer(sample.opt("sequence")) || !integer(sample.opt("elapsedMs")) ||
                    !(sample.opt("deltaG") instanceof Number) || !(sample.opt("gyroDps") instanceof Number)) return;
                if (sample.getLong("protocolVersion") != 1 ||
                    (sample.has("sampleAgeMs") && (!integer(sample.opt("sampleAgeMs")) || sample.getLong("sampleAgeMs") < 0 || sample.getLong("sampleAgeMs") > 0xffffffffL)) ||
                    (sample.has("droppedSamples") && (!integer(sample.opt("droppedSamples")) || sample.getLong("droppedSamples") < 0))) return;
                if (sample.getBoolean("sensorFresh") && sample.optLong("sampleAgeMs", -1) > 300) return;
                long sequence = sample.getLong("sequence"), elapsed = sample.getLong("elapsedMs");
                if (!LiveMotionBuffer.valid(ownerAuthenticated, liveMotionCapable, SystemClock.elapsedRealtime() - lastSnapshotAt,
                    deviceId, bootId, sample.getInt("protocolVersion"), sample.optString("deviceId"), sample.optString("bootId"),
                    sequence, elapsed, sample.getDouble("deltaG"), sample.getDouble("gyroDps")) ||
                    sequence <= lastMotionSequence || elapsed < lastMotionElapsed) return;
                lastMotionSequence = sequence; lastMotionElapsed = elapsed;
                BleBridge.enqueue(message); return;
            }
            JSONObject event = message.optJSONObject("bagEvent");
            if ("event".equals(message.optString("type")) && event != null && event.optString("quality").contains("live_connected")) {
                if (!integer(event.opt("protocolVersion")) || event.getLong("protocolVersion") != 1 ||
                    !integer(event.opt("sequence")) || !integer(event.opt("elapsedMs")) ||
                    !event.optString("recordedAtUtc").isEmpty() ||
                    !LiveMotionBuffer.valid(ownerAuthenticated, liveEventsCapable, SystemClock.elapsedRealtime() - lastSnapshotAt,
                        deviceId, bootId, 1, event.optString("deviceId"), event.optString("bootId"),
                        event.getLong("sequence"), event.getLong("elapsedMs"), 0, 0)) return;
            }
            String ownerToken = message.optString("ownerToken", "");
            if (!ownerToken.isEmpty()) {
                if (!"claimOwner".equals(message.optString("operation")) || !message.optBoolean("ok") || deviceId.isEmpty()) throw new IllegalStateException("Unexpected owner token");
                tokens.save(deviceId, ownerToken); message.remove("ownerToken");
                BleBridge.notice("Owner credential saved securely on this phone. Waiting for the board's authorization confirmation.");
            }
            if (snapshot != null && !snapshot.optBoolean("authenticated") && !deviceId.isEmpty() && !authenticationAttempted) {
                String token = "";
                try { token = tokens.load(deviceId); }
                catch (Exception ignored) { authenticationAttempted = true; BleBridge.notice("Saved owner credential could not be opened. Use the board's physical ownership-reset procedure before enrolling this phone again."); }
                if (!token.isEmpty() && !"authenticate".equals(message.optString("operation"))) { authenticationAttempted = true; internalCommand("authenticate", token); }
            }
            BleBridge.enqueue(message);
            if (snapshot != null && "alarm".equals(snapshot.optString("guardState"))) {
                NotificationManager manager = (NotificationManager)getSystemService(NOTIFICATION_SERVICE);
                manager.notify(NOTIFICATION_ID, notification("Bag alarm reported. Open TravelAgent for the latest confirmed state."));
            }
        } catch (Exception exception) { BleBridge.notice("A device message could not be validated. Read the current snapshot or reconnect."); }
    }); }

    private static boolean integer(Object value) {
        return value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long;
    }

    private void claim() {
        if (!ready) { BleBridge.notice("Connect to the board before authorizing this phone."); return; }
        BleBridge.notice("Requesting ownership. The board must be disarmed and its physical pairing window open.");
        internalCommand("claimOwner", null);
    }

    private void internalCommand(String operation, String token) {
        JSONObject message = new JSONObject();
        BleBridge.put(message, "type", "command");
        BleBridge.put(message, "protocolVersion", 1); BleBridge.put(message, "deviceId", deviceId); BleBridge.put(message, "bootId", bootId);
        BleBridge.put(message, "id", "native-" + UUID.randomUUID()); BleBridge.put(message, "operation", operation);
        if (token != null) BleBridge.put(message, "ownerToken", token);
        queue(message.toString());
    }

    private void closeConnection(String status) {
        stopScan(); ++linkGeneration; ready = false; writing = false; discovering = false; authenticationAttempted = false; mtu = 23;
        writes.clear(); frames.clear(); selectedAddress = ""; deviceId = ""; bootId = ""; commandCharacteristic = null;
        ownerAuthenticated = false; liveMotionCapable = false; liveEventsCapable = false;
        lastSnapshotAt = 0; lastMotionSequence = 0; lastMotionElapsed = -1;
        BleBridge.clearMotion();
        BluetoothGatt current = gatt; gatt = null;
        if (current != null) { try { current.disconnect(); current.close(); } catch (RuntimeException ignored) { } }
        BleBridge.transport(status, false);
    }

    @Override public IBinder onBind(Intent intent) { return null; }
    @Override public void onDestroy() { closeConnection("Bluetooth service stopped; last confirmed board state retained."); main.removeCallbacksAndMessages(null); unregisterReceiver(bondReceiver); super.onDestroy(); }
}
