namespace CaptionTranslator.Captions
{
    /// <summary>Plain snapshot of a UI Automation element and its children, so parsing can be tested without a live UI.</summary>
    public sealed class UiNode
    {
        public UiNode(string controlType, string name, string automationId, string className, IReadOnlyList<UiNode> children)
        {
            this.ControlType = controlType;
            this.Name = name;
            this.AutomationId = automationId;
            this.ClassName = className;
            this.Children = children;
        }

        /// <summary>UI Automation control type without the "ControlType." prefix, e.g. "Text", "Group", "ListItem".</summary>
        public string ControlType { get; }

        public string Name { get; }

        public string AutomationId { get; }

        public string ClassName { get; }

        public IReadOnlyList<UiNode> Children { get; }

        public IEnumerable<UiNode> DescendantsAndSelf()
        {
            yield return this;
            foreach (UiNode child in this.Children)
            {
                foreach (UiNode node in child.DescendantsAndSelf())
                    yield return node;
            }
        }
    }
}
