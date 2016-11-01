﻿/* 
* Copyright (c) Marc Clifton
* The Code Project Open License (CPOL) 1.02
* http://www.codeproject.com/info/cpol10.aspx
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlowSharpLib
{
    public class MouseAction
    {
        public MouseController.MouseEvent MouseEvent { get; }
        public Point MousePosition { get; }
        public MouseButtons Buttons { get; }

        public MouseAction(MouseController.MouseEvent mouseEvent, Point mousePosition)
        {
            MouseEvent = mouseEvent;
            MousePosition = mousePosition;
            // Buttons = buttons;
        }
    }

    public class MouseRouter
    {
        public MouseController.RouteName RouteName { get; set; }
        public MouseController.MouseEvent MouseEvent { get; set; }
        public Func<bool> Condition { get; set; }
        public Action Action { get; set; }
        public Action Else { get; set; }
    }

    public class MouseController
    {
        // State information:
        public Point LastMousePosition { get; set; }
        public Point CurrentMousePosition { get; set; }
        public MouseButtons CurrentButtons { get; set; }
        public bool DraggingSurface { get; set; }
        public bool DraggingShapes { get; set; }
        public bool DraggingAnchor { get; set; }
        public bool DraggingOccurred { get; set; }
        public bool DraggingSurfaceOccurred { get; set; }
        public bool SelectingShapes { get; set; }
        public GraphicElement HoverShape { get; set; }
        public ShapeAnchor SelectedAnchor { get; set; }
        public GraphicElement SelectionBox { get; set; }
        public bool DraggingSelectionBox { get; set; }
        public Point StartSelectionPosition { get; set; }

        public BaseController Controller { get; protected set; }

        protected List<MouseRouter> router;
        protected List<GraphicElement> justAddedShape = new List<GraphicElement>();

        public enum MouseEvent
        {
            MouseDown,
            MouseUp,
            MouseMove,
        }

        public enum RouteName
        {
            CanvasFocus,
            StartDragSurface,
            EndDragSurface,
            EndDragSurfaceWithDeselect,
            DragSurface,
            StartDragSelectionBox,
            EndDragSelectionBox,
            DragSelectionBox,
            StartShapeDrag,
            EndShapeDrag,
            DragShapes,
            DragAnchor,
            HoverOverShape,
            ShowAnchors,
            ShowAnchorCursor,
            ClearAnchorCursor,
            HideAnchors,
            SelectSingleShapeMouseUp,
            SelectSingleShapeMouseDown,
            SelectSingleGroupedShape,
            AddSelectedShape,
            RemoveSelectedShape,
        }

        public MouseController(BaseController controller)
        {
            Controller = controller;
            router = new List<MouseRouter>();
        }

        public void HookMouseEvents()
        {
            Controller.Canvas.MouseDown += (sndr, args) => HandleEvent(new MouseAction(MouseEvent.MouseDown, args.Location));
            Controller.Canvas.MouseUp += (sndr, args) => HandleEvent(new MouseAction(MouseEvent.MouseUp, args.Location));
            Controller.Canvas.MouseMove += (sndr, args) => HandleEvent(new MouseAction(MouseEvent.MouseMove, args.Location));
        }

        public void ShapeDeleted(GraphicElement el)
        {
            if (HoverShape == el)
            {
                DraggingSurface = false;
                HoverShape.ShowAnchors = false;
                HoverShape = null;
            }
        }

        public virtual void InitializeBehavior()
        {
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.CanvasFocus,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => true,
                Action = () =>
                {
                    // So Ctrl+V paste works, as keystroke is intercepted only when canvas panel has focus.
                    Controller.Canvas.Focus();
                }
            });

            // DRAG SURFACE ROUTES:

            // Start drag surface:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.StartDragSurface,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => !Controller.IsRootShapeSelectable(CurrentMousePosition) && CurrentButtons == MouseButtons.Left,
                Action = () =>
                {
                    DraggingSurface = true;
                    DraggingSurfaceOccurred = false;
                }
            });

            // End drag surface with no dragging, which deselects all selected shapes
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.EndDragSurfaceWithDeselect,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => DraggingSurface && !DraggingSurfaceOccurred,
                Action = () =>
                {
                    Controller.DeselectCurrentSelectedElements();
                    DraggingSurface = false;
                    Controller.Canvas.Cursor = Cursors.Arrow;
                }
            });

            // End drag surface when dragging occurred, selected shapes stay selected.
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.EndDragSurface,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => DraggingSurface && DraggingSurfaceOccurred,
                Action = () =>
                {
                    DraggingSurface = false;
                    DraggingSurfaceOccurred = false;
                    Controller.Canvas.Cursor = Cursors.Arrow;
                }
            });

            // Drag surface:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.DragSurface,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => DraggingSurface,
                Action = () =>
                {
                    DraggingSurfaceOccurred = true;
                    DragCanvas();
                }
            });

            // SHAPE DRAGGING ROUTES:

            // Start shape drag:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.StartShapeDrag,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    CurrentButtons == MouseButtons.Left &&
                    Controller.GetRootShapeAt(CurrentMousePosition).GetAnchors().FirstOrDefault(a => a.Near(CurrentMousePosition)) == null &&
                    !Controller.IsChildShapeSelectable(CurrentMousePosition),       // can't drag a grouped shape
                Action = () => DraggingShapes = true
            });

            // Start anchor drag:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.StartShapeDrag,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    CurrentButtons == MouseButtons.Left &&
                    Controller.GetRootShapeAt(CurrentMousePosition).GetAnchors().FirstOrDefault(a => a.Near(CurrentMousePosition)) != null,
                Action = () =>
                {
                    DraggingAnchor = true;
                    SelectedAnchor = HoverShape.GetAnchors().First(a => a.Near(CurrentMousePosition));
                },
            });

            // End shape/anchor dragging:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.EndShapeDrag,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => DraggingShapes || DraggingAnchor,
                Action = () =>
                {
                    Controller.HideConnectionPoints();
                    DraggingShapes = false;
                    // DraggingOccurred = false;        / Will be cleared by RemoveSelectedShape but this is order dependent!  TODO: Fix this somehow! :)
                    DraggingAnchor = false;
                    SelectedAnchor = null;
                    Controller.Canvas.Cursor = Cursors.Arrow;
                }
            });

            // Drag shapes:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.DragShapes,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => DraggingShapes && 
                    HoverShape != null && 
                    HoverShape.GetAnchors().FirstOrDefault(a => a.Near(CurrentMousePosition)) == null,
                Action = () =>
                {
                    DragShapes();
                    DraggingOccurred = true;
                },
            });

            // Drag anchor:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.DragAnchor,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => HoverShape != null && DraggingAnchor,
                Action = () =>
                {
                    DragAnchor();
                },
            });

            // HOVER ROUTES

            // Show anchors when hovering over a shape
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.HoverOverShape,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => !DraggingSurface && !DraggingShapes && !SelectingShapes && HoverShape == null &&
                    CurrentButtons == MouseButtons.None &&
                    Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    Controller.GetRootShapeAt(CurrentMousePosition).Parent == null, // no anchors for grouped children.
                Action = () => ShowAnchors(),
            });

            // Change anchors when hover shape changes
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.ShowAnchors,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => !DraggingSurface && !DraggingShapes && !SelectingShapes && HoverShape != null &&
                    CurrentButtons == MouseButtons.None &&
                    Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    HoverShape != Controller.GetRootShapeAt(CurrentMousePosition) &&
                    Controller.GetRootShapeAt(CurrentMousePosition).Parent == null, // no anchors for grouped children.
                Action = () => ChangeAnchors(),
            });

            // Hide anchors when not hovering over a shape
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.HideAnchors,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => !DraggingSurface && !DraggingShapes && !SelectingShapes && HoverShape != null &&
                    CurrentButtons == MouseButtons.None &&
                    !Controller.IsRootShapeSelectable(CurrentMousePosition),
                Action = () => HideAnchors(),
            });

            // Show cursor when hovering over an anchor
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.ShowAnchorCursor,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => !DraggingSurface && !DraggingShapes && !SelectingShapes && !DraggingAnchor && HoverShape != null &&
                    HoverShape.GetAnchors().FirstOrDefault(a => a.Near(CurrentMousePosition)) != null,
                Action = () => SetAnchorCursor(),
            });

            // Clear cursor when hovering over an anchor
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.ClearAnchorCursor,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => !DraggingSurface && !DraggingShapes && !SelectingShapes && HoverShape != null &&
                    HoverShape.GetAnchors().FirstOrDefault(a => a.Near(CurrentMousePosition)) == null,
                Action = () => ClearAnchorCursor(),
            });

            // SHAPE SELECTION

            // Select a shape
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.SelectSingleShapeMouseDown,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    !Controller.IsChildShapeSelectable(CurrentMousePosition) &&
                    !Controller.IsMultiSelect() &&
                    !Controller.SelectedElements.Contains(Controller.GetRootShapeAt(CurrentMousePosition)),
                Action = () => SelectSingleRootShape()
            });

            // Select a single grouped shape:
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.SelectSingleGroupedShape,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => Controller.IsChildShapeSelectable(CurrentMousePosition) &&
                    !Controller.IsMultiSelect() &&
                    !Controller.SelectedElements.Contains(Controller.GetChildShapeAt(CurrentMousePosition)),
                Action = () => SelectSingleChildShape()
            });

            // Select a single shape
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.SelectSingleShapeMouseUp,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    !Controller.IsChildShapeSelectable(CurrentMousePosition) &&     // Don't deselect grouped shape on mouse up (as in, don't select groupbox)
                    !Controller.IsMultiSelect() &&
                    !DraggingOccurred && !DraggingSelectionBox,
                Action = () => SelectSingleRootShape()
            });

            // Add another shape to selection list
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.AddSelectedShape,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    Controller.IsMultiSelect() && !DraggingSelectionBox &&
                    !Controller.SelectedElements.Contains(Controller.GetRootShapeAt(CurrentMousePosition)),
                Action = () => AddShape(),
            });

            // Remove shape from selection list
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.RemoveSelectedShape,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => Controller.IsRootShapeSelectable(CurrentMousePosition) &&
                    Controller.IsMultiSelect() && !DraggingSelectionBox &&
                    // TODO: Would nice to avoid multiple GetShapeAt calls when processing conditions.  And not just here.
                    Controller.SelectedElements.Contains(Controller.GetRootShapeAt(CurrentMousePosition)) &&
                    !justAddedShape.Contains(Controller.GetRootShapeAt(CurrentMousePosition)) &&
                    !DraggingOccurred,
                Action = () => RemoveShape(),
                Else = () =>
                {
                    justAddedShape.Clear();
                    DraggingOccurred = false;
                }
            });

            // SELECTION BOX

            // Start selection box
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.StartDragSelectionBox,
                MouseEvent = MouseEvent.MouseDown,
                Condition = () => !Controller.IsRootShapeSelectable(CurrentMousePosition) && CurrentButtons == MouseButtons.Right,
                Action = () =>
                {
                    DraggingSelectionBox = true;
                    StartSelectionPosition = CurrentMousePosition;
                    CreateSelectionBox();                
                },
            });

            // End selection box
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.EndDragSelectionBox,
                MouseEvent = MouseEvent.MouseUp,
                Condition = () => DraggingSelectionBox,
                Action = () =>
                {
                    DraggingSelectionBox = false;
                    SelectShapesInSelectionBox();
                }
            });

            // Drag selection box
            router.Add(new MouseRouter()
            {
                RouteName = RouteName.DragSelectionBox,
                MouseEvent = MouseEvent.MouseMove,
                Condition = () => DraggingSelectionBox,
                Action = () => DragSelectionBox(),
            });
        }

        protected virtual void HandleEvent(MouseAction action)
        {
            CurrentMousePosition = action.MousePosition;
            CurrentButtons = Control.MouseButtons;
            IEnumerable<MouseRouter> routes = router.Where(r => r.MouseEvent == action.MouseEvent);

            routes.ForEach(r =>
            {
                // Test condition every time after executing a route handler, as the handler may change state for the next condition.
                if (r.Condition())
                {
                    Trace.WriteLine("Route:" + r.RouteName.ToString());
                    r.Action();
                }
                else
                {
                    r.Else?.Invoke();
                }
            });

            LastMousePosition = CurrentMousePosition;
        }

        protected virtual void DragCanvas()
        {
            Point delta = CurrentMousePosition.Delta(LastMousePosition);
            Controller.Canvas.Cursor = Cursors.SizeAll;
            // Pick up every object on the canvas and move it.
            // This does not "move" the grid.
            Controller.MoveAllElements(delta);

            // Conversely, we redraw the grid and invalidate, which forces all the elements to redraw.
            //canvas.Drag(delta);
            //elements.ForEach(el => el.Move(delta));
            //canvas.Invalidate();
        }

        protected void ShowAnchors()
        {
            GraphicElement el = Controller.GetRootShapeAt(CurrentMousePosition);
            el.ShowAnchors = true;
            Controller.Redraw(el);
            HoverShape = el;
            Controller.SetAnchorCursor(el);
        }

        protected void ChangeAnchors()
        {
            HoverShape.ShowAnchors = false;
            Controller.Redraw(HoverShape);
            HoverShape = Controller.GetRootShapeAt(CurrentMousePosition);
            HoverShape.ShowAnchors = true;
            Controller.Redraw(HoverShape);
            Controller.SetAnchorCursor(HoverShape);
        }

        protected void HideAnchors()
        {
            HoverShape.ShowAnchors = false;
            Controller.Redraw(HoverShape);
            Controller.Canvas.Cursor = Cursors.Arrow;
            HoverShape = null;
        }

        protected void SelectSingleRootShape()
        {
            Controller.DeselectCurrentSelectedElements();
            GraphicElement el = Controller.GetRootShapeAt(CurrentMousePosition);
            Controller.SelectElement(el);
        }

        protected void SelectSingleChildShape()
        {
            Controller.DeselectCurrentSelectedElements();
            GraphicElement el = Controller.GetChildShapeAt(CurrentMousePosition);
            Controller.SelectElement(el);
        }

        protected void AddShape()
        {
            Controller.DeselectGroupedElements();
            GraphicElement el = Controller.GetRootShapeAt(CurrentMousePosition);
            Controller.SelectElement(el);
            justAddedShape.Add(el);
        }

        protected void RemoveShape()
        {
            GraphicElement el = Controller.GetRootShapeAt(CurrentMousePosition);
            Controller.DeselectElement(el);
        }

        protected void DragShapes()
        {
            Point delta = CurrentMousePosition.Delta(LastMousePosition);
            Controller.DragSelectedElements(delta);
            Controller.Canvas.Cursor = Cursors.SizeAll;
        }

        protected void ClearAnchorCursor()
        {
            Controller.Canvas.Cursor = Cursors.Arrow;
        }

        protected void SetAnchorCursor()
        {
            ShapeAnchor anchor = HoverShape.GetAnchors().FirstOrDefault(a => a.Near(CurrentMousePosition));

            // Hover shape could have changed as we move from a shape to a connector's anchor.
            if (anchor != null)
            {
                Controller.Canvas.Cursor = anchor.Cursor;
            }
        }

        protected void DragAnchor()
        {
            Point delta = CurrentMousePosition.Delta(LastMousePosition);
            bool connectorAttached = HoverShape.SnapCheck(SelectedAnchor, delta);

            if (!connectorAttached)
            {
                HoverShape.DisconnectShapeFromConnector(SelectedAnchor.Type);
                HoverShape.RemoveConnection(SelectedAnchor.Type);
            }
        }

        protected void CreateSelectionBox()
        {
            SelectionBox = new Box(Controller.Canvas);
            SelectionBox.BorderPen.Color = Color.Gray;
            SelectionBox.FillBrush.Color = Color.Transparent;
            SelectionBox.DisplayRectangle = new Rectangle(StartSelectionPosition, new Size(1, 1));
            Controller.Insert(SelectionBox);
        }

        protected void SelectShapesInSelectionBox()
        {
            Controller.DeleteElement(SelectionBox);
            List<GraphicElement> selectedElements = new List<GraphicElement>();

            Controller.Elements.Where(e => !selectedElements.Contains(e) && e.Parent == null && e.UpdateRectangle.IntersectsWith(SelectionBox.DisplayRectangle)).ForEach((e) =>
            {
                selectedElements.Add(e);
            });

            Controller.DeselectCurrentSelectedElements();
            Controller.SelectElements(selectedElements);
            Controller.Canvas.Invalidate();
        }

        protected void DragSelectionBox()
        {
            // Normalize the rectangle to a top-left, bottom-right rectangle.
            int x = CurrentMousePosition.X.Min(StartSelectionPosition.X);
            int y = CurrentMousePosition.Y.Min(StartSelectionPosition.Y);
            int w = (CurrentMousePosition.X - StartSelectionPosition.X).Abs();
            int h = (CurrentMousePosition.Y - StartSelectionPosition.Y).Abs();
            Rectangle newRect = new Rectangle(x, y, w, h);
            Point delta = CurrentMousePosition.Delta(LastMousePosition);
            Controller.UpdateDisplayRectangle(SelectionBox, newRect, delta);
        }
    }
}
