﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Tools;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK;

namespace osu.Game.Screens.Edit.Compose.Components
{
    public class BlueprintContainer : CompositeDrawable
    {
        public event Action<IEnumerable<HitObject>> SelectionChanged;

        private DragBox dragBox;
        private SelectionBlueprintContainer selectionBlueprints;
        private Container<PlacementBlueprint> placementBlueprintContainer;
        private PlacementBlueprint currentPlacement;
        private SelectionHandler selectionHandler;
        private InputManager inputManager;

        [Resolved]
        private HitObjectComposer composer { get; set; }

        [Resolved]
        private IEditorBeatmap beatmap { get; set; }

        public BlueprintContainer()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            selectionHandler = composer.CreateSelectionHandler();
            selectionHandler.DeselectAll = deselectAll;

            InternalChildren = new[]
            {
                dragBox = new DragBox(select),
                selectionHandler,
                selectionBlueprints = new SelectionBlueprintContainer { RelativeSizeAxes = Axes.Both },
                placementBlueprintContainer = new Container<PlacementBlueprint> { RelativeSizeAxes = Axes.Both },
                dragBox.CreateProxy()
            };

            foreach (var obj in composer.HitObjects)
                addBlueprintFor(obj);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            beatmap.HitObjectAdded += addBlueprintFor;
            beatmap.HitObjectRemoved += removeBlueprintFor;

            inputManager = GetContainingInputManager();
        }

        private HitObjectCompositionTool currentTool;

        /// <summary>
        /// The current placement tool.
        /// </summary>
        public HitObjectCompositionTool CurrentTool
        {
            get => currentTool;
            set
            {
                if (currentTool == value)
                    return;

                currentTool = value;

                refreshTool();
            }
        }

        private void addBlueprintFor(HitObject hitObject)
        {
            var drawable = composer.HitObjects.FirstOrDefault(d => d.HitObject == hitObject);
            if (drawable == null)
                return;

            addBlueprintFor(drawable);
        }

        private void removeBlueprintFor(HitObject hitObject)
        {
            var blueprint = selectionBlueprints.Single(m => m.DrawableObject.HitObject == hitObject);
            if (blueprint == null)
                return;

            blueprint.Deselect();

            blueprint.Selected -= onBlueprintSelected;
            blueprint.Deselected -= onBlueprintDeselected;
            blueprint.SelectionRequested -= onSelectionRequested;
            blueprint.DragRequested -= onDragRequested;

            selectionBlueprints.Remove(blueprint);
        }

        private void addBlueprintFor(DrawableHitObject hitObject)
        {
            refreshTool();

            var blueprint = composer.CreateBlueprintFor(hitObject);
            if (blueprint == null)
                return;

            blueprint.Selected += onBlueprintSelected;
            blueprint.Deselected += onBlueprintDeselected;
            blueprint.SelectionRequested += onSelectionRequested;
            blueprint.DragRequested += onDragRequested;

            selectionBlueprints.Add(blueprint);
        }

        private void removeBlueprintFor(DrawableHitObject hitObject) => removeBlueprintFor(hitObject.HitObject);

        protected override bool OnClick(ClickEvent e)
        {
            deselectAll();
            return true;
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            if (currentPlacement != null)
            {
                updatePlacementPosition(e.ScreenSpaceMousePosition);
                return true;
            }

            return base.OnMouseMove(e);
        }

        protected override void Update()
        {
            base.Update();

            if (currentPlacement != null)
            {
                if (composer.CursorInPlacementArea)
                    currentPlacement.State = PlacementState.Shown;
                else if (currentPlacement?.PlacementBegun == false)
                    currentPlacement.State = PlacementState.Hidden;
            }
        }

        /// <summary>
        /// Refreshes the current placement tool.
        /// </summary>
        private void refreshTool()
        {
            placementBlueprintContainer.Clear();
            currentPlacement = null;

            var blueprint = CurrentTool?.CreatePlacementBlueprint();

            if (blueprint != null)
            {
                placementBlueprintContainer.Child = currentPlacement = blueprint;

                // Fixes a 1-frame position discrepancy due to the first mouse move event happening in the next frame
                updatePlacementPosition(inputManager.CurrentState.Mouse.Position);
            }
        }

        private void updatePlacementPosition(Vector2 screenSpacePosition)
        {
            Vector2 snappedGridPosition = composer.GetSnappedPosition(ToLocalSpace(screenSpacePosition));
            Vector2 snappedScreenSpacePosition = ToScreenSpace(snappedGridPosition);

            currentPlacement.UpdatePosition(snappedScreenSpacePosition);
        }

        /// <summary>
        /// Select all masks in a given rectangle selection area.
        /// </summary>
        /// <param name="rect">The rectangle to perform a selection on in screen-space coordinates.</param>
        private void select(RectangleF rect)
        {
            foreach (var blueprint in selectionBlueprints)
            {
                if (blueprint.IsAlive && blueprint.IsPresent && rect.Contains(blueprint.SelectionPoint))
                    blueprint.Select();
                else
                    blueprint.Deselect();
            }
        }

        /// <summary>
        /// Deselects all selected <see cref="SelectionBlueprint"/>s.
        /// </summary>
        private void deselectAll() => selectionHandler.SelectedBlueprints.ToList().ForEach(m => m.Deselect());

        private void onBlueprintSelected(SelectionBlueprint blueprint)
        {
            selectionHandler.HandleSelected(blueprint);
            selectionBlueprints.ChangeChildDepth(blueprint, 1);

            SelectionChanged?.Invoke(selectionHandler.SelectedHitObjects);
        }

        private void onBlueprintDeselected(SelectionBlueprint blueprint)
        {
            selectionHandler.HandleDeselected(blueprint);
            selectionBlueprints.ChangeChildDepth(blueprint, 0);

            SelectionChanged?.Invoke(selectionHandler.SelectedHitObjects);
        }

        private void onSelectionRequested(SelectionBlueprint blueprint, InputState state) => selectionHandler.HandleSelectionRequested(blueprint, state);

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (!selectionHandler.SelectedBlueprints.Any(b => b.IsHovered))
                dragBox.FadeIn(250, Easing.OutQuint);

            return true;
        }

        protected override bool OnDrag(DragEvent e)
        {
            dragBox.UpdateDrag(e);
            return true;
        }

        protected override bool OnDragEnd(DragEndEvent e)
        {
            dragBox.FadeOut(250, Easing.OutQuint);
            selectionHandler.UpdateVisibility();

            return true;
        }

        private void onDragRequested(SelectionBlueprint blueprint, DragEvent dragEvent)
        {
            HitObject draggedObject = blueprint.DrawableObject.HitObject;

            Vector2 movePosition = blueprint.ScreenSpaceMovementStartPosition + dragEvent.ScreenSpaceMousePosition - dragEvent.ScreenSpaceMouseDownPosition;
            Vector2 snappedPosition = composer.GetSnappedPosition(ToLocalSpace(movePosition));

            // Move the hitobjects
            selectionHandler.HandleMovement(new MoveSelectionEvent(blueprint, blueprint.ScreenSpaceMovementStartPosition, ToScreenSpace(snappedPosition)));

            // Apply the start time at the newly snapped-to position
            double offset = composer.GetSnappedTime(draggedObject.StartTime, snappedPosition) - draggedObject.StartTime;
            foreach (HitObject obj in selectionHandler.SelectedHitObjects)
                obj.StartTime += offset;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (beatmap != null)
            {
                beatmap.HitObjectAdded -= addBlueprintFor;
                beatmap.HitObjectRemoved -= removeBlueprintFor;
            }
        }

        private class SelectionBlueprintContainer : Container<SelectionBlueprint>
        {
            protected override int Compare(Drawable x, Drawable y)
            {
                if (!(x is SelectionBlueprint xBlueprint) || !(y is SelectionBlueprint yBlueprint))
                    return base.Compare(x, y);

                return Compare(xBlueprint, yBlueprint);
            }

            public int Compare(SelectionBlueprint x, SelectionBlueprint y)
            {
                // dpeth is used to denote selected status (we always want selected blueprints to handle input first).
                int d = x.Depth.CompareTo(y.Depth);
                if (d != 0)
                    return d;

                // Put earlier hitobjects towards the end of the list, so they handle input first
                int i = y.DrawableObject.HitObject.StartTime.CompareTo(x.DrawableObject.HitObject.StartTime);
                return i == 0 ? CompareReverseChildID(x, y) : i;
            }
        }
    }
}
