import com.travelagent.ble.GatewaySettingsBridge;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import javax.crypto.*;

/** Synthetic-only settings validation and real AES-GCM round-trip/tamper tests; no device/credentials. */
public final class GatewaySettingsTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        String url="http://192.168.1.20:8788", token="SYNTHETIC-LOCAL-TEST-BEARER-NOT-A-CREDENTIAL";
        for(String endpoint:new String[]{url,"https://gateway.example.test/private","http://[::1]:8788"}) {
            GatewaySettingsBridge.Validation.require(endpoint,token);check(true,"valid HTTP/HTTPS gateway base accepted");
        }
        for(String endpoint:new String[]{"file:///private/token","https://name:pass@example.test","http:///missing-host","http://example.test:70000","http://example.test:0","http://example.test\r\nInjected:header","","data:text/plain,value"})
            reject(()->GatewaySettingsBridge.Validation.require(endpoint,token),"invalid/credential-bearing gateway address rejected");
        reject(()->GatewaySettingsBridge.Validation.require("http://example.test/"+"x".repeat(2048),token),"URL length bounded");
        for(String invalid:new String[]{"","x\ny","x\ry","x\ty","x y","x\0y","non-ASCII-ä","x".repeat(4097)})
            reject(()->GatewaySettingsBridge.Validation.require(url,invalid),"unsafe header value or oversized token rejected");
        GatewaySettingsBridge.Validation.require(url,"x".repeat(4096));check(true,"maximum valid token accepted");
        KeyGenerator generator=KeyGenerator.getInstance("AES");generator.init(256);
        var key=generator.generateKey();byte[] aad="com.travelagent.test:gateway-settings:v1".getBytes(StandardCharsets.UTF_8);
        String sealed=GatewaySettingsBridge.Codec.seal(url,token,key,aad);
        check(!sealed.contains(url)&&!sealed.contains(token),"stored envelope contains neither plaintext URL nor bearer");
        var opened=GatewaySettingsBridge.Codec.open(sealed,key,aad);
        check(opened.url.equals(url)&&opened.token.equals(token),"one authenticated record round-trips exact URL/bearer pair");
        check(!sealed.equals(GatewaySettingsBridge.Codec.seal(url,token,key,aad)),"fresh random GCM nonce changes ciphertext for identical settings");
        Path persisted=Files.createTempFile("travelagent-synthetic-settings-",".sealed");
        try {Files.writeString(persisted,sealed);check(GatewaySettingsBridge.Codec.open(Files.readString(persisted),key,aad).token.equals(token),"sealed settings round-trip after disk read");}
        finally {Files.deleteIfExists(persisted);}
        reject(()->GatewaySettingsBridge.Codec.open(sealed,generator.generateKey(),aad),"wrong key cannot return credentials");
        reject(()->GatewaySettingsBridge.Codec.open(sealed,key,"different-purpose".getBytes(StandardCharsets.UTF_8)),"package/purpose AAD prevents cross-context reuse");
        String[] parts=sealed.split(":");byte[] corrupted=Base64.getDecoder().decode(parts[2]);corrupted[0]^=1;
        String altered=parts[0]+":"+parts[1]+":"+Base64.getEncoder().encodeToString(corrupted);
        reject(()->GatewaySettingsBridge.Codec.open(altered,key,aad),"tampered ciphertext cannot yield partial credentials");
        for(String invalid:new String[]{"",sealed+":extra","2:"+parts[1]+":"+parts[2],"1:AA==:"+parts[2],"1:not-base64:invalid","x".repeat(24577)})
            reject(()->GatewaySettingsBridge.Codec.open(invalid,key,aad),"malformed or oversized envelope rejected without plaintext fallback");
        System.out.println("PASS: "+checks+" gateway settings validation/encryption assertions");
    }
    private static void check(boolean condition,String message){checks++;if(!condition)throw new AssertionError(message);}
    private static void reject(Checked action,String message)throws Exception{try{action.run();}catch(Exception expected){checks++;return;}throw new AssertionError(message);}
    private interface Checked{void run()throws Exception;}
}
