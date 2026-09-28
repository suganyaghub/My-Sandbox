using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class CaptionTrackerTest
    {
        private static readonly DateTimeOffset start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        private readonly CaptionTracker tracker = new CaptionTracker(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1));

        [TestMethod]
        public void Process_SameBubbleRevised_UpdatesInsteadOfAddingANewCard()
        {
            CaptionTrackerResult first = this.tracker.Process(Bubbles(("b1", "Anna", "Wenn ich jetzt")), start);
            CaptionTrackerResult second = this.tracker.Process(Bubbles(("b1", "Anna", "Dann kann ich jetzt zum Beispiel prüfen")), start.AddMilliseconds(500));

            Assert.IsTrue(first.Updates.Single().IsNew);
            CaptionUpdate update = second.Updates.Single();
            Assert.IsFalse(update.IsNew);
            Assert.AreEqual("b1", update.Id);
            Assert.AreEqual("Dann kann ich jetzt zum Beispiel prüfen", update.Text);
        }

        [TestMethod]
        public void Process_Liveness_NewestChangingBubbleIsLiveUntilQuietOrANewerBubble()
        {
            Assert.IsTrue(this.tracker.Process(Bubbles(("b1", "Anna", "Hallo")), start).Updates.Single().IsLive);

            CaptionUpdate quiet = this.tracker.Process(Bubbles(("b1", "Anna", "Hallo")), start.AddSeconds(2)).Updates.Single();
            Assert.IsFalse(quiet.IsLive);

            this.tracker.Process(Bubbles(("b1", "Anna", "Hallo zusammen")), start.AddSeconds(3));
            CaptionTrackerResult newer = this.tracker.Process(Bubbles(("b1", "Anna", "Hallo zusammen"), ("b2", "Ben", "Guten")), start.AddSeconds(3.2));
            Assert.IsFalse(newer.Updates.Single(update => update.Id == "b1").IsLive);
            Assert.IsTrue(newer.Updates.Single(update => update.Id == "b2").IsLive);
        }

        [TestMethod]
        public void Process_FinishedSentence_SettledOnceAfterSettlePeriod()
        {
            List<SettledSentence> settled = Run(
                (0.0, "Das ist der erste Satz. Und dann"),
                (0.5, "Das ist der erste Satz. Und dann"),
                (1.1, "Das ist der erste Satz. Und dann kommt"),
                (2.0, "Das ist der erste Satz. Und dann kommt"));

            CollectionAssert.AreEqual(new[] { "Das ist der erste Satz." }, settled.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_PunctuationChangedAfterReading_NotReadAgain()
        {
            // Recorded pattern: "…jetzt damit." first, then Teams inserts a comma and extends it by one word.
            List<SettledSentence> settled = Run(
                (0.0, "Die Tests laufen jetzt damit."),
                (1.1, "Die Tests laufen jetzt damit."),
                (1.5, "Die Tests laufen jetzt, damit das."),
                (3.0, "Die Tests laufen jetzt, damit das."));

            CollectionAssert.AreEqual(new[] { "Die Tests laufen jetzt damit." }, settled.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_SentenceExtendedByManyWords_ReadsOnlyTheNewEnd()
        {
            List<SettledSentence> settled = Run(
                (0.0, "Die Tests laufen jetzt damit."),
                (1.1, "Die Tests laufen jetzt damit."),
                (1.5, "Die Tests laufen jetzt, damit wir morgen sauber weitermachen."),
                (3.0, "Die Tests laufen jetzt, damit wir morgen sauber weitermachen."));

            CollectionAssert.AreEqual(new[] { "Die Tests laufen jetzt damit.", "wir morgen sauber weitermachen." }, settled.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_SentenceExtendedTwiceAndPolledRepeatedly_EachNewPartReadExactlyOnce()
        {
            // Recorded pattern: a spoken sentence loses its full stop and grows twice; polls continue every 250 ms.
            List<SettledSentence> settled = Run(
                (0.0, "Also wenn ich jetzt kurz zurück gehe."),
                (1.1, "Also wenn ich jetzt kurz zurück gehe."),
                (1.2, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version."),
                (2.3, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version."),
                (2.6, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version."),
                (2.9, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version Gut, dann probieren wir es neu."),
                (4.0, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version Gut, dann probieren wir es neu."),
                (4.3, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version Gut, dann probieren wir es neu."),
                (4.6, "Also wenn ich jetzt kurz zurück gehe das war leider die alte Version Gut, dann probieren wir es neu."));

            CollectionAssert.AreEqual(
                new[] { "Also wenn ich jetzt kurz zurück gehe.", "das war leider die alte Version.", "Gut, dann probieren wir es neu." },
                settled.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_WordsCorrectedAfterReading_NotReadAgain()
        {
            // Recorded pattern: Teams replaces the first words of a sentence it already showed.
            List<SettledSentence> settled = Run(
                (0.0, "Wir wollen prüfen, ob die neue Version so läuft, wie wir es geplant haben."),
                (1.1, "Wir wollen prüfen, ob die neue Version so läuft, wie wir es geplant haben."),
                (1.5, "Oder wollen prüfen, ob die neue Version so läuft, wie wir es geplant haben."),
                (3.0, "Oder wollen prüfen, ob die neue Version so läuft, wie wir es geplant haben."));

            Assert.AreEqual(1, settled.Count);
        }

        [TestMethod]
        public void Process_HeavilyCorrectedSentence_NotReadAgain()
        {
            // Recorded pattern: pronoun, verb form and the end are corrected, but most words stay in the same order.
            List<SettledSentence> settled = Run(
                (0.0, "Also man sollte jetzt nie im gleichen Fenster sagen, jetzt starten wir das."),
                (1.1, "Also man sollte jetzt nie im gleichen Fenster sagen, jetzt starten wir das."),
                (1.5, "Also ihr solltet jetzt nie im gleichen Fenster sagen, jetzt start mal das."),
                (3.0, "Also ihr solltet jetzt nie im gleichen Fenster sagen, jetzt start mal das."));

            Assert.AreEqual(1, settled.Count);
        }

        [TestMethod]
        public void Process_SentenceCorrectedAndContinued_ReadsOnlyTheContinuation()
        {
            // Recorded pattern: the first part of a read sentence is corrected, then 14 new words are added.
            List<SettledSentence> settled = Run(
                (0.0, "Also man sollte jetzt nie im gleichen Fenster sagen, jetzt starten wir den Test, weil eigentlich alles."),
                (1.1, "Also man sollte jetzt nie im gleichen Fenster sagen, jetzt starten wir den Test, weil eigentlich alles."),
                (1.5, "Also ihr solltet jetzt nie im gleichen Fenster sagen, jetzt start mal den Test, weil eigentlich alles, was vorher besprochen wurde, noch im Speicher liegt."),
                (3.0, "Also ihr solltet jetzt nie im gleichen Fenster sagen, jetzt start mal den Test, weil eigentlich alles, was vorher besprochen wurde, noch im Speicher liegt."));

            CollectionAssert.AreEqual(
                new[] { "Also man sollte jetzt nie im gleichen Fenster sagen, jetzt starten wir den Test, weil eigentlich alles.", "was vorher besprochen wurde, noch im Speicher liegt." },
                settled.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_NewSentenceSharingCommonWords_IsStillRead()
        {
            List<SettledSentence> settled = Run(
                (0.0, "Ich habe das in der Session gestern schon gezeigt."),
                (1.1, "Ich habe das in der Session gestern schon gezeigt."),
                (1.5, "Ich habe das in der Session gestern schon gezeigt. Heute zeige ich euch das neue Setup für die Tests."),
                (3.0, "Ich habe das in der Session gestern schon gezeigt. Heute zeige ich euch das neue Setup für die Tests."));

            Assert.AreEqual(2, settled.Count);
        }

        [TestMethod]
        public void OrderedSimilarity_CorrectedVersusDifferentSentence_HighVersusLow()
        {
            string[] original = CaptionText.Words("Also man sollte jetzt nie im gleichen Fenster sagen, jetzt starten wir das.");
            string[] corrected = CaptionText.Words("Also ihr solltet jetzt nie im gleichen Fenster sagen, jetzt start mal das.");
            string[] different = CaptionText.Words("Und dann sagen wir jetzt einfach mal, das passt so.");

            Assert.IsTrue(CaptionText.OrderedSimilarity(original, corrected) >= 0.6);
            Assert.IsTrue(CaptionText.OrderedSimilarity(original, different) < 0.6);
        }

        [TestMethod]
        public void Process_SentencesMergedAfterReading_NotReadAgain()
        {
            List<SettledSentence> settled = Run(
                (0.0, "Na, er soll diese Datei. Anlegen mit dem Namen."),
                (1.1, "Na, er soll diese Datei. Anlegen mit dem Namen."),
                (1.5, "Er soll diese Datei anlegen mit dem Namen."),
                (3.0, "Er soll diese Datei anlegen mit dem Namen."));

            CollectionAssert.AreEqual(new[] { "Na, er soll diese Datei.", "Anlegen mit dem Namen." }, settled.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_UnfinishedLastSentence_SettledOnlyWhenTheBubbleStopsBeingLive()
        {
            List<SettledSentence> whileLive = Run((0.0, "Und dann machen wir"), (1.2, "Und dann machen wir"));
            List<SettledSentence> afterQuiet = Run((1.6, "Und dann machen wir"));

            Assert.AreEqual(0, whileLive.Count);
            CollectionAssert.AreEqual(new[] { "Und dann machen wir" }, afterQuiet.Select(sentence => sentence.Text).ToArray());
        }

        [TestMethod]
        public void Process_IgnoredBacklogBubble_NeitherShownNorRead()
        {
            this.tracker.Ignore(new[] { "old" });

            CaptionTrackerResult result = this.tracker.Process(Bubbles(("old", "Anna", "Alter Satz."), ("new", "Ben", "Neuer Satz.")), start.AddSeconds(5));
            CaptionTrackerResult later = this.tracker.Process(Bubbles(("old", "Anna", "Alter Satz."), ("new", "Ben", "Neuer Satz.")), start.AddSeconds(7));

            Assert.AreEqual("new", result.Updates.Single().Id);
            Assert.IsFalse(later.Settled.Any(sentence => sentence.Id == "old"));
        }

        [TestMethod]
        public void Process_SameSentenceInTwoBubbles_ReadInBoth()
        {
            this.tracker.Process(Bubbles(("b1", "Anna", "Ja."), ("b2", "Anna", "Ja.")), start);
            CaptionTrackerResult result = this.tracker.Process(Bubbles(("b1", "Anna", "Ja."), ("b2", "Anna", "Ja.")), start.AddSeconds(2));

            Assert.AreEqual(2, result.Settled.Count);
        }

        private List<SettledSentence> Run(params (double Seconds, string Text)[] steps)
        {
            List<SettledSentence> settled = new List<SettledSentence>();
            foreach ((double seconds, string text) in steps)
                settled.AddRange(this.tracker.Process(Bubbles(("b1", "Anna", text)), start.AddSeconds(seconds)).Settled);

            return settled;
        }

        private static IReadOnlyList<CaptionSegment> Bubbles(params (string Id, string Speaker, string Text)[] bubbles)
            => bubbles.Select(bubble => new CaptionSegment(bubble.Speaker, bubble.Text, bubble.Id)).ToList();
    }
}
