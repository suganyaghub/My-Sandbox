using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class TeamsCaptionParserTest
    {
        [TestMethod]
        public void Parse_WrappedListOfItems_ReturnsSpeakerAndText()
        {
            UiNode container = Node("Group", "Live Captions",
                Node("List", "",
                    Node("ListItem", "", Text("Anna Muster"), Node("Group", "", Text("Guten Morgen"), Text("zusammen."))),
                    Node("ListItem", "", Text("Ben Beispiel"), Text("Hallo."))));

            IReadOnlyList<CaptionSegment> segments = TeamsCaptionParser.Parse(container);

            CollectionAssert.AreEqual(
                new[] { new CaptionSegment("Anna Muster", "Guten Morgen zusammen."), new CaptionSegment("Ben Beispiel", "Hallo.") },
                segments.ToArray());
        }

        [TestMethod]
        public void Parse_ButtonsAndNestedDuplicateTexts_AreIgnored()
        {
            UiNode container = Node("Group", "Untertitel",
                Node("Button", "Untertiteleinstellungen", Text("Einstellungen")),
                Node("Group", "", Text("Anna"), Node("Text", "Wie geht's?", Text("Wie geht's?"))));

            IReadOnlyList<CaptionSegment> segments = TeamsCaptionParser.Parse(container);

            CollectionAssert.AreEqual(new[] { new CaptionSegment("Anna", "Wie geht's?") }, segments.ToArray());
        }

        [TestMethod]
        public void Parse_ItemWithSingleText_HasEmptySpeaker()
        {
            UiNode container = Node("Group", "Captions", Node("Group", "", Text("Nur Text.")), Node("Group", "", Text("Mehr.")));

            IReadOnlyList<CaptionSegment> segments = TeamsCaptionParser.Parse(container);

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(string.Empty, segments[0].Speaker);
        }

        [TestMethod]
        public void Parse_RealTeamsPaneStructure_ReturnsOnlyCaptionItems()
        {
            // Shape recorded from new Teams (2026-09): pane header + buttons, then the list several groups deep,
            // with an empty group before every caption item.
            UiNode container = Node("Group", "Live Captions",
                Node("Text", "Live Captions", Text("Live Captions")),
                Node("Button", "Give live captions feedback Thumb up"),
                Node("Button", "Pop out captions"),
                Node("Group", "Live Captions",
                    Node("Group", "",
                        Node("Group", "",
                            Node("Group", "",
                                Node("Group", ""),
                                Node("Group", "", Text("Max Beispiel"), Text("Das ist der nächste Punkt.")),
                                Node("Group", ""),
                                Node("Group", "", Text("Anna Muster"), Text("Genau.")))))));

            IReadOnlyList<CaptionSegment> segments = TeamsCaptionParser.Parse(container);

            CollectionAssert.AreEqual(
                new[] { new CaptionSegment("Max Beispiel", "Das ist der nächste Punkt."), new CaptionSegment("Anna Muster", "Genau.") },
                segments.ToArray());
        }

        private static UiNode Text(string name) => new UiNode("Text", name, string.Empty, string.Empty, Array.Empty<UiNode>());

        private static UiNode Node(string controlType, string name, params UiNode[] children) => new UiNode(controlType, name, string.Empty, string.Empty, children);
    }
}
