namespace TravelAgent.Core
{
    public enum SequenceEntryPhase { Arm, RepeatArm, Disarm, RepeatDisarm, Complete }

    /// <summary>Captures proposed settings only. It never changes confirmed device configuration.</summary>
    public sealed class SequenceEntryModel
    {
        private readonly string[] entries = { "", "", "", "" };

        public SequenceEntryPhase Phase { get; private set; } = SequenceEntryPhase.Arm;
        public string Message { get; private set; } = "";
        public string ArmSequence => entries[0];
        public string RepeatArmSequence => entries[1];
        public string DisarmSequence => entries[2];
        public string RepeatDisarmSequence => entries[3];
        public bool IsComplete => Phase == SequenceEntryPhase.Complete;
        public string ActiveEntry => entries[IsComplete ? 3 : (int)Phase];
        public int Progress => ActiveEntry.Length;
        public int TapCount => entries[0].Length + entries[1].Length + entries[2].Length + entries[3].Length;
        public bool CanUndo => TapCount > 0;

        public bool Tap(char symbol)
        {
            if (symbol != 'A' && symbol != 'B')
            {
                Message = "Use A or B only.";
                return false;
            }
            if (IsComplete) return false;

            Message = "";
            int index = (int)Phase;
            entries[index] += symbol;
            if (entries[index].Length < 4) return true;

            if (Phase == SequenceEntryPhase.RepeatArm && entries[1] != entries[0])
            {
                entries[1] = "";
                Message = "Repeat arm didn't match. Try again.";
                return false;
            }
            if (Phase == SequenceEntryPhase.Disarm && entries[2] == entries[0])
            {
                entries[2] = "";
                Message = "Disarm must differ. Choose another code.";
                return false;
            }
            if (Phase == SequenceEntryPhase.RepeatDisarm && entries[3] != entries[2])
            {
                entries[3] = "";
                Message = "Repeat disarm didn't match. Try again.";
                return false;
            }

            Phase = (SequenceEntryPhase)(index + 1);
            return true;
        }

        /// <summary>Undo one accepted tap, invalidating later confirmations if a phase boundary is crossed.</summary>
        public bool Undo()
        {
            if (!CanUndo) return false;
            int index = IsComplete ? 3 : (int)Phase;
            while (index >= 0 && entries[index].Length == 0) index--;
            if (index < 0) return false;
            entries[index] = entries[index].Substring(0, entries[index].Length - 1);
            for (int later = index + 1; later < entries.Length; later++) entries[later] = "";
            Phase = (SequenceEntryPhase)index;
            Message = "";
            return true;
        }

        public void Reset()
        {
            for (int i = 0; i < entries.Length; i++) entries[i] = "";
            Phase = SequenceEntryPhase.Arm;
            Message = "";
        }
    }
}
