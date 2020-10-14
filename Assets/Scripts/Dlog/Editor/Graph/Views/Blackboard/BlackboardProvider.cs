using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dlog {
    public class BlackboardProvider {
        private static readonly Texture2D exposedIcon = Resources.Load<Texture2D>("GraphView/Nodes/BlackboardFieldExposed");
        public Blackboard Blackboard { get; private set; }
        private readonly EditorView editorView;
        private readonly Dictionary<string, BlackboardRow> inputRows;
        private readonly BlackboardSection checksTriggersSection;
        private readonly BlackboardSection actorSection;
        private List<Node> selectedNodes = new List<Node>();
        private Dictionary<string, bool> expandedInputs = new Dictionary<string, bool>();

        public Dictionary<string, bool> ExpandedInputs => expandedInputs;
        public string AssetName {
            get => Blackboard.title;
            set => Blackboard.title = value;
        }

        public BlackboardProvider(EditorView editorView) {
            this.editorView = editorView;
            inputRows = new Dictionary<string, BlackboardRow>();

            Blackboard = new Blackboard {
                scrollable = true,
                title = "Events and Properties",
                subTitle = "Dialogue Properties",
                editTextRequested = EditTextRequested,
                addItemRequested = AddItemRequested,
                moveItemRequested = MoveItemRequested
            };

            checksTriggersSection = new BlackboardSection {title = "Triggers & Checks"};
            Blackboard.Add(checksTriggersSection);
            actorSection = new BlackboardSection {title = "Actors"};
            Blackboard.Add(actorSection);
        }

        private void EditTextRequested(Blackboard blackboard, VisualElement visualElement, string newText) {
            var field = (BlackboardField) visualElement;
            var property = (AbstractProperty) field.userData;
            if (!string.IsNullOrEmpty(newText) && newText != property.DisplayName) {
                editorView.DlogObject.RegisterCompleteObjectUndo("Edit Property Name");
                property.DisplayName = newText;
                editorView.DlogObject.DlogGraph.SanitizePropertyName(property);
                field.text = property.DisplayName;
                // TODO: Change name of created property nodes
            }
        }

        private void MoveItemRequested(Blackboard blackboard, int newIndex, VisualElement visualElement) {
            if (!(visualElement.userData is AbstractProperty property))
                return;

            editorView.DlogObject.RegisterCompleteObjectUndo("Move Property");
            editorView.DlogObject.DlogGraph.MoveProperty(property, newIndex);
        }

        private void AddItemRequested(Blackboard blackboard) {
            var menu = new GenericMenu();
            AddSectionAItems(menu);
            AddSectionBItems(menu);
            menu.ShowAsContext();
        }

        private void AddSectionAItems(GenericMenu menu) {
            menu.AddItem(new GUIContent($"Trigger"), false, () => AddInputRow(new TriggerProperty(), true));
            menu.AddItem(new GUIContent($"Check"), false, () => AddInputRow(new CheckProperty(), true));
            menu.AddSeparator($"/");
        }

        private void AddSectionBItems(GenericMenu menu) {
            menu.AddItem(new GUIContent($"Actor"), false, () => AddInputRow(new ActorProperty(), true));
            menu.AddSeparator($"/");
        }

        public void AddInputRow(AbstractProperty property, bool create = false, int index = -1) {
            if (inputRows.ContainsKey(property.GUID))
                return;

            var section = property.Type == PropertyType.Actor ? actorSection : checksTriggersSection;

            if (create) {
                editorView.DlogObject.DlogGraph.SanitizePropertyName(property);
            }

            var field = new BlackboardField(exposedIcon, property.DisplayName, property.Type.ToString()) {userData = property};
            var row = new BlackboardRow(field, new BlackboardPropertyView(field, editorView, property)) {userData = property};
            if (index < 0)
                index = inputRows.Count;
            if (index == inputRows.Count)
                section.Add(row);
            else
                section.Insert(index, row);

            var pill = row.Q<Pill>();
            pill.RegisterCallback<MouseEnterEvent>(evt => OnMouseHover(evt, property));
            pill.RegisterCallback<MouseLeaveEvent>(evt => OnMouseHover(evt, property));
            pill.RegisterCallback<DragUpdatedEvent>(OnDragUpdatedEvent);

            var expandButton = row.Q<Button>("expandButton");
            expandButton.RegisterCallback<MouseDownEvent>(evt => OnExpanded(evt, property), TrickleDown.TrickleDown);

            inputRows[property.GUID] = row;
            if (create) {
                row.expanded = true;
                expandedInputs[property.GUID] = true;
                editorView.DlogObject.RegisterCompleteObjectUndo("Create Property");
                editorView.DlogObject.DlogGraph.AddProperty(property);
                field.OpenTextEditor();
            }
        }

        private void OnExpanded(MouseDownEvent evt, AbstractProperty input) {
            expandedInputs[input.GUID] = !inputRows[input.GUID].expanded;
        }

        private void OnMouseHover(EventBase evt, AbstractProperty input) {
            if (evt.eventTypeId == MouseEnterEvent.TypeId()) {
                foreach (var node in editorView.GraphView.nodes.ToList()) {
                    if (node.viewDataKey == input.GUID) {
                        selectedNodes.Add(node);
                        node.AddToClassList("hovered");
                    }
                }
            } else if (evt.eventTypeId == MouseLeaveEvent.TypeId() && selectedNodes.Any()) {
                foreach (var node in selectedNodes) {
                    node.RemoveFromClassList("hovered");
                }

                selectedNodes.Clear();
            }
        }

        private void OnDragUpdatedEvent(DragUpdatedEvent evt) {
            if (selectedNodes.Any()) {
                foreach (var node in selectedNodes) {
                    node.RemoveFromClassList("hovered");
                }

                selectedNodes.Clear();
            }
        }

        public void HandleChanges() {
            foreach (var inputGuid in editorView.DlogObject.DlogGraph.RemovedProperties) {
                if (!inputRows.TryGetValue(inputGuid.GUID, out var row))
                    continue;

                row.RemoveFromHierarchy();
                inputRows.Remove(inputGuid.GUID);
            }

            foreach (var input in editorView.DlogObject.DlogGraph.AddedProperties)
                AddInputRow(input, index: editorView.DlogObject.DlogGraph.Properties.IndexOf(input));

            if (editorView.DlogObject.DlogGraph.MovedProperties.Any()) {
                foreach (var row in inputRows.Values)
                    row.RemoveFromHierarchy();

                foreach (var property in editorView.DlogObject.DlogGraph.Properties)
                    (property.Type == PropertyType.Actor ? actorSection : checksTriggersSection).Add(inputRows[property.GUID]);
            }

            expandedInputs.Clear();
        }
    }
}