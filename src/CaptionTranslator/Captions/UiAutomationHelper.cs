using System.Diagnostics;
using System.Text;
using System.Windows.Automation;

namespace CaptionTranslator.Captions
{
    /// <summary>UI Automation plumbing shared by the caption sources.</summary>
    public static class UiAutomationHelper
    {
        public static IReadOnlyList<AutomationElement> FindTopLevelWindows(params string[] processNames)
        {
            List<int> processIds = new List<int>();
            foreach (string processName in processNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    processIds.Add(process.Id);
                    process.Dispose();
                }
            }

            List<AutomationElement> windows = new List<AutomationElement>();
            if (processIds.Count == 0)
                return windows;

            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try
                {
                    if (processIds.Contains(window.Current.ProcessId))
                        windows.Add(window);
                }
                catch (Exception exception) when (TeamsCaptionSource.IsTransient(exception))
                {
                    // Window closed while enumerating.
                }
            }

            return windows;
        }

        /// <summary>Reads the whole subtree of an element in one cross-process call and converts it to <see cref="UiNode"/>.</summary>
        public static UiNode Snapshot(AutomationElement element)
        {
            CacheRequest request = new CacheRequest
            {
                TreeScope = TreeScope.Subtree,
                TreeFilter = Automation.RawViewCondition,
                AutomationElementMode = AutomationElementMode.None,
            };
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.ControlTypeProperty);

            AutomationElement cached = element.GetUpdatedCache(request);
            return Convert(cached, 0);
        }

        public static string Format(UiNode node)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, node, 0);
            return builder.ToString();
        }

        private static UiNode Convert(AutomationElement cached, int depth)
        {
            List<UiNode> children = new List<UiNode>();
            if (depth < 80)
            {
                foreach (AutomationElement child in cached.CachedChildren)
                    children.Add(Convert(child, depth + 1));
            }

            ControlType? controlType = cached.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
            string controlTypeName = controlType?.ProgrammaticName.Replace("ControlType.", string.Empty) ?? string.Empty;

            return new UiNode(
                controlTypeName,
                cached.GetCachedPropertyValue(AutomationElement.NameProperty) as string ?? string.Empty,
                cached.GetCachedPropertyValue(AutomationElement.AutomationIdProperty) as string ?? string.Empty,
                cached.GetCachedPropertyValue(AutomationElement.ClassNameProperty) as string ?? string.Empty,
                children);
        }

        private static void Append(StringBuilder builder, UiNode node, int indent)
        {
            builder.Append(' ', indent * 2)
                   .Append(node.ControlType)
                   .Append(" | Name=\"").Append(node.Name.Replace("\r", " ").Replace("\n", " ")).Append('"')
                   .Append(" | Id=\"").Append(node.AutomationId).Append('"')
                   .Append(" | Class=\"").Append(node.ClassName).Append('"')
                   .AppendLine();

            foreach (UiNode child in node.Children)
                Append(builder, child, indent + 1);
        }
    }
}
