using System.Collections.Generic;
using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared helper for shape preview functionality.
    /// Centralizes two-point selection logic used by ShapePlacementState.
    /// </summary>
    public class ShapePreviewHelper
    {
        // State fields
        private IntVec3? firstCorner = null;
        private IntVec3? secondCorner = null;
        private List<IntVec3> previewCells = new List<IntVec3>();
        private ShapeType currentShape = ShapeType.FilledRectangle;

        // Sound feedback tracking
        private int lastCellCount = 0;
        private float lastDragRealTime = 0f;

        // Properties
        public IntVec3? FirstCorner => firstCorner;
        public IntVec3? SecondCorner => secondCorner;
        public IReadOnlyList<IntVec3> PreviewCells => previewCells;
        public ShapeType CurrentShape => currentShape;
        public bool HasFirstCorner => firstCorner.HasValue;
        public bool IsInPreviewMode => firstCorner.HasValue && secondCorner.HasValue;

        public void SetCurrentShape(ShapeType shape)
        {
            currentShape = shape;
        }

        public void SetFirstCorner(IntVec3 cell, string context = "", bool silent = false)
        {
            firstCorner = cell;
            secondCorner = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;
            if (!silent)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Preview.FirstPoint".Loc(cell.x, cell.z));
            }
        }

        public void SetSecondCorner(IntVec3 cell, string context = "", bool silent = false, string extraInfo = null)
        {
            if (!firstCorner.HasValue)
            {
                Log.Warning($"{context}: SetSecondCorner called without first point set");
                return;
            }

            secondCorner = cell;
            previewCells = ShapeHelper.CalculateCells(currentShape, firstCorner.Value, cell);

            if (!silent)
            {
                string sizeText = ShapeHelper.FormatShapeSize(previewCells);
                // For regular rectangles, add cell count; for irregular shapes it's already in the size text
                string announcement = ShapeHelper.IsRegularRectangle(previewCells)
                    ? (string)"RimWorldAccess.Building.Preview.SecondPointRegular".Translate(sizeText, previewCells.Count)
                    : (string)"RimWorldAccess.Building.Preview.SecondPointIrregular".Translate(sizeText);

                if (!extraInfo.NullOrEmpty())
                {
                    announcement += ". " + extraInfo;
                }

                TolkHelper.SpeakData(announcement);
            }
        }

        /// <summary>
        /// Grows the preview to <paramref name="cursor"/>. Returns the new extent ("3 by 4") when
        /// the shape actually changed size, otherwise null — the caller speaks it as the lead-in to
        /// the cell announcement so the extent and where it ends arrive as one utterance.
        /// </summary>
        public string UpdatePreview(IntVec3 cursor)
        {
            if (!firstCorner.HasValue) return null;

            secondCorner = cursor;
            previewCells = ShapeHelper.CalculateCells(currentShape, firstCorner.Value, cursor);

            int cellCount = previewCells.Count;
            if (cellCount == lastCellCount)
                return null;

            PlayDragSound();
            lastCellCount = cellCount;

            // During drag we use corners since we don't know actual cells yet
            return ShapeHelper.FormatShapeSizeFromCorners(firstCorner.Value, cursor);
        }

        public List<IntVec3> ConfirmShape(string context = "")
        {
            if (!IsInPreviewMode)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Paint.NoShapeToConfirm".Loc());
                return new List<IntVec3>();
            }

            var confirmedCells = new List<IntVec3>(previewCells);
            // FormatShapeSize returns "W by H" for regular rectangles, "N cells" for irregular shapes
            string sizeText = ShapeHelper.FormatShapeSize(confirmedCells);

            TolkHelper.Speak("RimWorldAccess.Building.Preview.SizeConfirmed".Loc(sizeText));

            // Reset for next selection
            Reset();

            return confirmedCells;
        }

        public void Cancel()
        {
            if (!HasFirstCorner) return;

            Reset();
            TolkHelper.Speak("RimWorldAccess.Building.Preview.ShapeCancelled".Loc());
        }

        public void Reset()
        {
            firstCorner = null;
            secondCorner = null;
            previewCells.Clear();
            lastCellCount = 0;
            lastDragRealTime = Time.realtimeSinceStartup;
            // Keep currentShape as is
        }

        public void FullReset()
        {
            Reset();
            currentShape = ShapeType.FilledRectangle;
        }

        private void PlayDragSound()
        {
            SoundInfo info = SoundInfo.OnCamera();
            info.SetParameter("TimeSinceDrag", Time.realtimeSinceStartup - lastDragRealTime);
            SoundDefOf.Designate_DragStandard_Changed.PlayOneShot(info);
            lastDragRealTime = Time.realtimeSinceStartup;
        }
    }
}
