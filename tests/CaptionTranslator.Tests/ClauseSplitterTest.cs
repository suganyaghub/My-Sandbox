using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class ClauseSplitterTest
    {
        [TestMethod]
        public void Split_CommaAndFullStop_ReturnsClausesWithPunctuation()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("Good morning everyone, let's start.");

            CollectionAssert.AreEqual(new[] { new Clause("Good morning everyone", ','), new Clause("let's start", '.') }, clauses.ToArray());
        }

        [TestMethod]
        public void Split_DecimalNumber_DoesNotSplitInsideTheNumber()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("It costs 3.5 million euros.");

            CollectionAssert.AreEqual(new[] { new Clause("It costs 3.5 million euros", '.') }, clauses.ToArray());
        }

        [TestMethod]
        public void Split_SeveralMarks_KeepsTheLastMark()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("Really?! Yes");

            CollectionAssert.AreEqual(new[] { new Clause("Really", '!'), new Clause("Yes", null) }, clauses.ToArray());
        }

        [TestMethod]
        public void Split_TextWithoutWords_ReturnsNothing()
        {
            Assert.IsEmpty(ClauseSplitter.Split(""));
            Assert.IsEmpty(ClauseSplitter.Split(" ... "));
        }

        [TestMethod]
        public void Split_SurroundingSpaces_AreTrimmed()
        {
            IReadOnlyList<Clause> clauses = ClauseSplitter.Split("  Hello  ");

            CollectionAssert.AreEqual(new[] { new Clause("Hello", null) }, clauses.ToArray());
        }
    }
}
