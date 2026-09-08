using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>Shape a scanner's subcategory type must expose for the shared cursor to walk it.</summary>
    public interface IScannerSubcategory<TItem>
    {
        List<TItem> Items { get; }
        bool IsEmpty { get; }
    }

    /// <summary>Shape a scanner's category type must expose for the shared cursor to walk it.</summary>
    public interface IScannerCategory<TSubcategory>
    {
        List<TSubcategory> Subcategories { get; }
    }

    /// <summary>
    /// The index-cursor skeleton shared by the map scanner (<c>ScannerState</c>) and the world
    /// scanner (<c>WorldScannerState</c>): a 3-level category/subcategory/item cursor plus a 4th
    /// "extra" index (the map's bulk-group index / the world's region-instance index), save and
    /// restore of the whole cursor for temporary-category detours, and temporary-category
    /// bookkeeping. Distance sorting, announcements and jumping stay with each scanner: they
    /// differ enough (live vs. capped traversal distance; terrain- vs. biome-region jump targets)
    /// that unifying them costs more clarity than it saves.
    /// <see cref="ScannerNavSession{TPosition}"/> is the paired navigation-session half.
    /// </summary>
    public class ScannerCursor<TCategory, TSubcategory, TItem>
        where TCategory : class, IScannerCategory<TSubcategory>
        where TSubcategory : class, IScannerSubcategory<TItem>
        where TItem : class
    {
        public List<TCategory> Categories = new List<TCategory>();
        public int CategoryIndex;
        public int SubcategoryIndex;
        public int ItemIndex;
        public int ExtraIndex; // bulk index (map) / instance index (world)

        private int savedCategoryIndex = -1;
        private int savedSubcategoryIndex = -1;
        private int savedItemIndex = -1;
        private int savedExtraIndex = -1;

        public TCategory TemporaryCategory { get; private set; }

        public TCategory GetCurrentCategory()
        {
            if (CategoryIndex < 0 || CategoryIndex >= Categories.Count)
                return null;
            return Categories[CategoryIndex];
        }

        public TSubcategory GetCurrentSubcategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return null;
            var subs = category.Subcategories;
            if (SubcategoryIndex < 0 || SubcategoryIndex >= subs.Count)
                return null;
            return subs[SubcategoryIndex];
        }

        public TItem GetCurrentItem()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return null;
            var items = subcat.Items;
            if (ItemIndex < 0 || ItemIndex >= items.Count)
                return null;
            return items[ItemIndex];
        }

        /// <summary>Saves all four indices for later <see cref="RestoreFocus"/>.</summary>
        public void SaveFocus()
        {
            savedCategoryIndex = CategoryIndex;
            savedSubcategoryIndex = SubcategoryIndex;
            savedItemIndex = ItemIndex;
            savedExtraIndex = ExtraIndex;
        }

        /// <summary>
        /// Restores the saved cursor position, then validates it against the current category
        /// list, which may have changed since the save. No-op if nothing was saved.
        /// </summary>
        public void RestoreFocus(Func<TItem, int> extraCount = null)
        {
            if (savedCategoryIndex < 0) return;

            CategoryIndex = savedCategoryIndex;
            SubcategoryIndex = savedSubcategoryIndex;
            ItemIndex = savedItemIndex;
            ExtraIndex = savedExtraIndex;

            savedCategoryIndex = -1;
            savedSubcategoryIndex = -1;
            savedItemIndex = -1;
            savedExtraIndex = -1;

            ValidateIndices(extraCount);
        }

        /// <summary>
        /// Clamps every index into range, skipping to the first non-empty subcategory. Call after
        /// any operation that may have changed the category list. <paramref name="extraCount"/>,
        /// when given, also clamps ExtraIndex against the current item's instance/bulk count.
        /// </summary>
        public void ValidateIndices(Func<TItem, int> extraCount = null)
        {
            if (CategoryIndex < 0 || CategoryIndex >= Categories.Count)
                CategoryIndex = 0;

            var category = GetCurrentCategory();
            if (category != null)
            {
                var subs = category.Subcategories;
                if (SubcategoryIndex < 0 || SubcategoryIndex >= subs.Count)
                    SubcategoryIndex = 0;

                SkipEmptySubcategories(forward: true);
            }

            var subcat = GetCurrentSubcategory();
            if (subcat != null)
            {
                if (ItemIndex < 0 || ItemIndex >= subcat.Items.Count)
                    ItemIndex = 0;
            }

            if (extraCount != null)
            {
                var item = GetCurrentItem();
                if (item != null)
                {
                    int count = extraCount(item);
                    if (ExtraIndex < 0 || ExtraIndex >= count)
                        ExtraIndex = 0;
                }
            }
        }

        /// <summary>
        /// Walks SubcategoryIndex to the nearest non-empty subcategory, wrapping in the requested
        /// direction. Restores the starting index if every subcategory is empty.
        /// </summary>
        public void SkipEmptySubcategories(bool forward)
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            var subs = category.Subcategories;
            int startIndex = SubcategoryIndex;
            int attempts = 0;
            int maxAttempts = subs.Count;

            while ((GetCurrentSubcategory()?.IsEmpty ?? true) && attempts < maxAttempts)
            {
                if (forward)
                {
                    SubcategoryIndex++;
                    if (SubcategoryIndex >= subs.Count)
                        SubcategoryIndex = 0;
                }
                else
                {
                    SubcategoryIndex--;
                    if (SubcategoryIndex < 0)
                        SubcategoryIndex = subs.Count - 1;
                }

                attempts++;
            }

            if (GetCurrentSubcategory()?.IsEmpty ?? true)
                SubcategoryIndex = startIndex;
        }

        // --- Temporary category bookkeeping ---

        public bool IsInTemporaryCategory()
        {
            return TemporaryCategory != null && GetCurrentCategory() == TemporaryCategory;
        }

        /// <summary>
        /// Registers <paramref name="category"/> as the temporary category and inserts it at the
        /// FRONT of Categories (always the first stop of the category cycle) without touching the
        /// cursor indices: refresh paths carry over indices already taken against a front-temp
        /// list, so no compensation belongs here, and a caller attaching into a list the cursor is
        /// already using compensates itself. Callers remove any previous temporary category first.
        /// </summary>
        public void AttachTemporaryCategory(TCategory category)
        {
            TemporaryCategory = category;
            Categories.Insert(0, category);
        }

        /// <summary>
        /// <see cref="AttachTemporaryCategory"/> plus selecting it at the first
        /// subcategory/item/extra index. Callers remove any previous temporary category first.
        /// </summary>
        public void SelectTemporaryCategory(TCategory category)
        {
            AttachTemporaryCategory(category);
            CategoryIndex = 0;
            SubcategoryIndex = 0;
            ItemIndex = 0;
            ExtraIndex = 0;
        }

        /// <summary>
        /// Removes the temporary category from Categories if one exists, returning whether one
        /// was removed. A cursor resting on a real category past the removed slot shifts down with
        /// it so it keeps naming the same category; a cursor that was ON the temporary category is
        /// left for the caller to re-point.
        /// </summary>
        public bool RemoveTemporaryCategory()
        {
            if (TemporaryCategory == null) return false;
            int removedAt = Categories.IndexOf(TemporaryCategory);
            Categories.Remove(TemporaryCategory);
            TemporaryCategory = null;
            if (removedAt >= 0 && CategoryIndex > removedAt)
                CategoryIndex--;
            return true;
        }

        /// <summary>
        /// Clears the temporary-category reference without touching Categories, for a list already
        /// rebuilt from scratch.
        /// </summary>
        public void ClearTemporaryCategoryReference()
        {
            TemporaryCategory = null;
        }

        /// <summary>Discards any pending saved focus, so a stale one cannot be restored.</summary>
        public void ClearSavedFocus()
        {
            savedCategoryIndex = -1;
            savedSubcategoryIndex = -1;
            savedItemIndex = -1;
            savedExtraIndex = -1;
        }

        /// <summary>Re-attaches the temporary category to Categories without moving the cursor.</summary>
        public void ReattachTemporaryCategoryIfMissing()
        {
            if (TemporaryCategory != null && !Categories.Contains(TemporaryCategory))
                Categories.Insert(0, TemporaryCategory);
        }

        /// <summary>
        /// Re-points the cursor onto the temporary category, clamping the desired indices.
        /// </summary>
        public void FocusTemporaryCategory(int desiredSubcategoryIndex, int desiredItemIndex)
        {
            if (TemporaryCategory == null || Categories.Count == 0)
                return;

            int index = Categories.IndexOf(TemporaryCategory);
            CategoryIndex = index >= 0 ? index : 0;
            var subs = TemporaryCategory.Subcategories;
            int subCount = subs?.Count ?? 0;
            SubcategoryIndex = subCount > 0
                ? Math.Min(Math.Max(desiredSubcategoryIndex, 0), subCount - 1)
                : 0;
            int itemCount = subCount > 0 ? (subs[SubcategoryIndex].Items?.Count ?? 0) : 0;
            ItemIndex = itemCount > 0
                ? Math.Min(Math.Max(desiredItemIndex, 0), itemCount - 1)
                : 0;
            ExtraIndex = 0;
        }
    }

    /// <summary>
    /// The navigation-session half of the scanner skeleton: within a Page Up/Down sequence the
    /// sort order, and any per-press-expensive computation such as traversal-distance pathfinding,
    /// is frozen so sequential navigation stays stable and fast. A session ends when the
    /// cursor/origin moves externally, the category/subcategory changes, or the underlying data
    /// changes. Generic over the position type (<c>IntVec3</c> for the map scanner,
    /// <c>PlanetTile</c> for the world scanner).
    /// </summary>
    public class ScannerNavSession<TPosition>
    {
        public bool Active { get; private set; }
        public TPosition LastPosition { get; private set; }
        private bool hasLastPosition;

        /// <summary>
        /// Set for the duration of a scanner-driven position write so the position-changed
        /// notification does not treat its own write as external drift.
        /// </summary>
        public bool ScannerDrivenJumpInProgress;

        /// <summary>Ends the session. The next navigation call re-sorts/re-anchors from scratch.</summary>
        public void Invalidate()
        {
            Active = false;
            hasLastPosition = false;
            LastPosition = default(TPosition);
        }

        /// <summary>
        /// Called on every write to the underlying position; invalidates the session unless the
        /// write is our own scanner-driven jump.
        /// </summary>
        public void NotifyPositionWritten()
        {
            if (!ScannerDrivenJumpInProgress)
                Invalidate();
        }

        /// <summary>Starts a fresh session anchored to <paramref name="currentPosition"/>.</summary>
        public void Begin(TPosition currentPosition)
        {
            Active = true;
            LastPosition = currentPosition;
            hasLastPosition = true;
        }

        /// <summary>
        /// Records where the session-tracked position ended up, without changing Active. Call
        /// after an in-session step so the next press's drift check compares against the
        /// post-step position.
        /// </summary>
        public void RecordPosition(TPosition currentPosition)
        {
            LastPosition = currentPosition;
            hasLastPosition = true;
        }

        /// <summary>
        /// True when a session is active but the live position no longer matches where the session
        /// last left it, meaning some code path bypassed the position-setter hook. Callers
        /// invalidate before proceeding.
        /// </summary>
        public bool HasDrifted(TPosition currentPosition)
        {
            return Active && hasLastPosition
                && !EqualityComparer<TPosition>.Default.Equals(currentPosition, LastPosition);
        }
    }
}
