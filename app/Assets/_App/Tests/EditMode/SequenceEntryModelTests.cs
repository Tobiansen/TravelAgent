using NUnit.Framework;
using TravelAgent.Core;

namespace TravelAgent.Tests
{
    public sealed class SequenceEntryModelTests
    {
        [Test]
        public void SixteenTapsCompleteBothDistinctSequencesAndBothRepeats()
        {
            var model = new SequenceEntryModel();
            Enter(model, "ABAB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.RepeatArm));
            Assert.That(model.IsComplete, Is.False);
            Enter(model, "ABAB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Disarm));
            Enter(model, "ABBA");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.RepeatDisarm));
            Enter(model, "ABBA");
            Assert.That(model.IsComplete, Is.True);
            Assert.That(model.TapCount, Is.EqualTo(16));
            Assert.That(model.ArmSequence, Is.EqualTo(model.RepeatArmSequence));
            Assert.That(model.DisarmSequence, Is.EqualTo(model.RepeatDisarmSequence));
            Assert.That(model.Tap('A'), Is.False);
            Assert.That(model.TapCount, Is.EqualTo(16));
        }

        [Test]
        public void ArmRepeatMismatchClearsOnlyItsRepeatAndCanBeRetried()
        {
            var model = new SequenceEntryModel();
            Enter(model, "AABB");
            Enter(model, "ABAB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.RepeatArm));
            Assert.That(model.ArmSequence, Is.EqualTo("AABB"));
            Assert.That(model.RepeatArmSequence, Is.Empty);
            Assert.That(model.DisarmSequence, Is.Empty);
            Assert.That(model.Message, Does.Contain("didn't match"));
            Enter(model, "AABB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Disarm));
        }

        [Test]
        public void DisarmRepeatMismatchPreservesTheOtherThreeEntries()
        {
            var model = new SequenceEntryModel();
            Enter(model, "AABBAABBBABA");
            Enter(model, "BBBB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.RepeatDisarm));
            Assert.That(model.ArmSequence, Is.EqualTo("AABB"));
            Assert.That(model.RepeatArmSequence, Is.EqualTo("AABB"));
            Assert.That(model.DisarmSequence, Is.EqualTo("BABA"));
            Assert.That(model.RepeatDisarmSequence, Is.Empty);
            Enter(model, "BABA");
            Assert.That(model.IsComplete, Is.True);
        }

        [Test]
        public void IdenticalDisarmIsRejectedBeforeRepeatWithoutLosingArmConfirmation()
        {
            var model = new SequenceEntryModel();
            Enter(model, "ABABABABABAB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Disarm));
            Assert.That(model.DisarmSequence, Is.Empty);
            Assert.That(model.ArmSequence, Is.EqualTo("ABAB"));
            Assert.That(model.RepeatArmSequence, Is.EqualTo("ABAB"));
            Assert.That(model.Message, Does.Contain("must differ"));
            Enter(model, "ABBAABBA");
            Assert.That(model.IsComplete, Is.True);
        }

        [Test]
        public void UndoCrossesPhaseBoundaryAndRequiresTheConfirmationAgain()
        {
            var model = new SequenceEntryModel();
            Enter(model, "ABABABAB");
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Disarm));
            Assert.That(model.Undo(), Is.True);
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.RepeatArm));
            Assert.That(model.RepeatArmSequence, Is.EqualTo("ABA"));
            Assert.That(model.ArmSequence, Is.EqualTo("ABAB"));
            model.Tap('B');
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Disarm));
        }

        [Test]
        public void UndoCompletedEntryReopensLastRepeatAndRestartClearsEveryPhase()
        {
            var model = new SequenceEntryModel();
            Enter(model, "AABBAABBBABABABA");
            Assert.That(model.IsComplete, Is.True);
            Assert.That(model.Undo(), Is.True);
            Assert.That(model.IsComplete, Is.False);
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.RepeatDisarm));
            Assert.That(model.RepeatDisarmSequence, Is.EqualTo("BAB"));
            model.Reset();
            Assert.That(model.TapCount, Is.Zero);
            Assert.That(model.CanUndo, Is.False);
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Arm));
            Assert.That(model.Message, Is.Empty);
            Assert.That(model.Undo(), Is.False);
        }

        [Test]
        public void NonABInputDoesNotCountOrChangeTheCurrentPhase()
        {
            var model = new SequenceEntryModel();
            Enter(model, "AB");
            Assert.That(model.Tap('X'), Is.False);
            Assert.That(model.Tap('a'), Is.False);
            Assert.That(model.ArmSequence, Is.EqualTo("AB"));
            Assert.That(model.Progress, Is.EqualTo(2));
            Assert.That(model.Phase, Is.EqualTo(SequenceEntryPhase.Arm));
        }

        private static void Enter(SequenceEntryModel model, string taps)
        {
            foreach (char symbol in taps) model.Tap(symbol);
        }
    }
}
