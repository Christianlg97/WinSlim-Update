using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wumgr
{
    internal sealed class ModernUpdateList : Control
    {
        internal sealed class ItemEventArgs : EventArgs
        {
            public ItemEventArgs(ListViewItem item) { Item = item; }
            public ListViewItem Item { get; private set; }
        }

        internal sealed class ColumnEventArgs : EventArgs
        {
            public ColumnEventArgs(int column) { Column = column; }
            public int Column { get; private set; }
        }

        private sealed class LayoutEntry
        {
            public bool IsGroup;
            public string GroupKey;
            public string GroupTitle;
            public ListViewItem Item;
            public int Top;
            public int Height;
        }

        private const int HeaderHeight = 32;
        private const int RowHeight = 36;
        private const int GroupHeight = 32;
        private const int ScrollBarSize = 18;
        private const int MinimumThumb = 34;
        private const int ColumnDividerTolerance = 5;

        private readonly Color background = Color.FromArgb(32, 32, 32);
        private readonly Color headerBackground = Color.FromArgb(43, 43, 43);
        private readonly Color rowEven = Color.FromArgb(31, 31, 31);
        private readonly Color rowOdd = Color.FromArgb(28, 28, 28);
        private readonly Color hoverBackground = Color.FromArgb(45, 45, 45);
        private readonly Color selectedBackground = Color.FromArgb(51, 51, 51);
        private readonly Color border = Color.FromArgb(58, 58, 58);
        private readonly Color text = Color.FromArgb(246, 246, 246);
        private readonly Color secondaryText = Color.FromArgb(205, 205, 205);
        private readonly Color muted = Color.FromArgb(154, 154, 154);
        private readonly Color accent = Color.FromArgb(224, 226, 230);
        private readonly Color danger = Color.FromArgb(226, 126, 132);
        private readonly Color scrollTrack = Color.FromArgb(38, 38, 38);
        private readonly Color scrollThumb = Color.FromArgb(176, 178, 182);
        private readonly Color scrollThumbHover = Color.FromArgb(218, 220, 224);

        private readonly List<ListViewItem> items = new List<ListViewItem>();
        private readonly List<LayoutEntry> layout = new List<LayoutEntry>();
        private readonly Dictionary<string, bool> collapsedGroups = new Dictionary<string, bool>();
        private readonly int[] baseColumnWidths = { 270, 105, 82, 76, 70, 150 };
        private readonly int[] minimumColumnWidths = { 150, 80, 70, 65, 60, 95 };
        private string[] headers = { "Actualización", "Categoría", "Artículo", "Fecha", "Tamaño", "Estado" };
        private Font regularFont;
        private Font semiboldFont;
        private Font emptyTitleFont;
        private int verticalOffset;
        private int horizontalOffset;
        private int contentHeight;
        private int contentWidth;
        private int hoverEntry = -1;
        private ListViewItem selectedItem;
        private bool draggingVertical;
        private bool draggingHorizontal;
        private int resizingColumn = -1;
        private int dragOrigin;
        private int dragOffsetOrigin;
        private int resizeOrigin;
        private int resizeWidthOrigin;
        private bool showGroups;
        private bool showCheckBoxes = true;
        private string emptyTitle = "Todo está al día";
        private string emptySubtitle = "No se encontraron actualizaciones pendientes.";

        public ModernUpdateList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            DoubleBuffered = true;
            TabStop = true;
            BackColor = background;
            ForeColor = text;
            regularFont = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            semiboldFont = new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold, GraphicsUnit.Point);
            emptyTitleFont = new Font("Segoe UI Semibold", 17F, FontStyle.Bold, GraphicsUnit.Point);
        }

        public event EventHandler<ItemEventArgs> ItemCheckedChanged;
        public event EventHandler<ItemEventArgs> SelectedItemChanged;
        public event EventHandler<ColumnEventArgs> ColumnClicked;

        public bool ShowGroups
        {
            get { return showGroups; }
            set { if (showGroups != value) { showGroups = value; RebuildLayout(); } }
        }

        public bool ShowCheckBoxes
        {
            get { return showCheckBoxes; }
            set { if (showCheckBoxes != value) { showCheckBoxes = value; Invalidate(); } }
        }

        public ListViewItem SelectedItem { get { return selectedItem; } }

        public void SetItems(IEnumerable<ListViewItem> source, bool groups, bool checkBoxes)
        {
            items.Clear();
            if (source != null)
            {
                foreach (ListViewItem item in source)
                    items.Add(item);
            }
            showGroups = groups;
            showCheckBoxes = checkBoxes;
            if (selectedItem != null && !items.Contains(selectedItem))
                selectedItem = null;
            verticalOffset = 0;
            horizontalOffset = 0;
            RebuildLayout();
        }

        public void SetHeaders(params string[] values)
        {
            if (values != null && values.Length >= 6)
            {
                headers = new string[6];
                Array.Copy(values, headers, 6);
                Invalidate();
            }
        }

        public void SetEmptyMessage(string titleValue, string subtitleValue)
        {
            emptyTitle = titleValue ?? string.Empty;
            emptySubtitle = subtitleValue ?? string.Empty;
            Invalidate();
        }

        public void SelectItem(ListViewItem item)
        {
            SetSelectedItem(item, true);
            EnsureSelectedVisible();
        }

        public void SortByColumn(int column, bool descending)
        {
            if (column < 0 || column >= 6)
                return;
            Comparison<ListViewItem> comparison = delegate(ListViewItem left, ListViewItem right)
            {
                int result;
                if (column == 3 && left.SubItems[column].Tag is DateTime && right.SubItems[column].Tag is DateTime)
                    result = ((DateTime)left.SubItems[column].Tag).CompareTo((DateTime)right.SubItems[column].Tag);
                else if (column == 4 && left.SubItems[column].Tag is decimal && right.SubItems[column].Tag is decimal)
                    result = ((decimal)left.SubItems[column].Tag).CompareTo((decimal)right.SubItems[column].Tag);
                else
                    result = string.Compare(left.SubItems[column].Text, right.SubItems[column].Text,
                        StringComparison.CurrentCultureIgnoreCase);
                return descending ? -result : result;
            };

            if (showGroups)
            {
                List<string> order = new List<string>();
                Dictionary<string, List<ListViewItem>> grouped = new Dictionary<string, List<ListViewItem>>();
                foreach (ListViewItem item in items)
                {
                    string key = GetGroupKey(item);
                    List<ListViewItem> groupItems;
                    if (!grouped.TryGetValue(key, out groupItems))
                    {
                        groupItems = new List<ListViewItem>();
                        grouped.Add(key, groupItems);
                        order.Add(key);
                    }
                    groupItems.Add(item);
                }
                items.Clear();
                foreach (string key in order)
                {
                    grouped[key].Sort(comparison);
                    items.AddRange(grouped[key]);
                }
            }
            else
                items.Sort(comparison);
            verticalOffset = 0;
            RebuildLayout();
        }

        public string GetTruncatedText(Point location)
        {
            int entryIndex = HitTestEntry(location);
            if (entryIndex < 0 || layout[entryIndex].IsGroup)
                return string.Empty;
            int column = HitTestColumn(location.X);
            if (column < 0)
                return string.Empty;
            ListViewItem item = layout[entryIndex].Item;
            if (item.SubItems.Count <= column)
                return string.Empty;
            string value = item.SubItems[column].Text ?? string.Empty;
            Rectangle bounds = GetCellBounds(layout[entryIndex], column);
            int padding = column == 0 && showCheckBoxes ? 43 : (column == 5 ? 31 : 17);
            int measured = TextRenderer.MeasureText(value, item.Font ?? regularFont, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            return measured > Math.Max(0, bounds.Width - padding) ? value : string.Empty;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                regularFont.Dispose();
                semiboldFont.Dispose();
                emptyTitleFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampOffsets();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(background);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle viewport = ContentViewport;
            GraphicsState state = graphics.Save();
            graphics.SetClip(viewport);
            if (items.Count == 0)
                DrawEmptyState(graphics, viewport);
            else
                DrawEntries(graphics, viewport);
            graphics.Restore(state);

            // TextRenderer usa GDI y puede ignorar parcialmente el clip de GDI+.
            // Repintar la cabecera al final garantiza que ninguna fila desplazada
            // deje texto residual sobre ella.
            DrawHeader(graphics);
            DrawScrollBars(graphics);
            if (Focused)
            {
                using (Pen focus = new Pen(Color.FromArgb(72, 72, 72)))
                    graphics.DrawRectangle(focus, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            }
            graphics.SmoothingMode = SmoothingMode.Default;
        }

        private Rectangle ContentViewport
        {
            get
            {
                return new Rectangle(0, HeaderHeight,
                    Math.Max(0, ClientSize.Width - ScrollBarSize),
                    Math.Max(0, ClientSize.Height - HeaderHeight - ScrollBarSize));
            }
        }

        private Rectangle VerticalTrack
        {
            get { return new Rectangle(Math.Max(0, ClientSize.Width - ScrollBarSize), HeaderHeight,
                ScrollBarSize, Math.Max(0, ClientSize.Height - HeaderHeight - ScrollBarSize)); }
        }

        private Rectangle HorizontalTrack
        {
            get { return new Rectangle(0, Math.Max(HeaderHeight, ClientSize.Height - ScrollBarSize),
                Math.Max(0, ClientSize.Width - ScrollBarSize), ScrollBarSize); }
        }

        private void RebuildLayout()
        {
            layout.Clear();
            int top = 0;
            string previousGroup = null;
            foreach (ListViewItem item in items)
            {
                string groupKey = GetGroupKey(item);
                if (showGroups && !string.Equals(previousGroup, groupKey, StringComparison.Ordinal))
                {
                    layout.Add(new LayoutEntry
                    {
                        IsGroup = true,
                        GroupKey = groupKey,
                        GroupTitle = GetGroupTitle(item),
                        Top = top,
                        Height = GroupHeight
                    });
                    top += GroupHeight;
                    previousGroup = groupKey;
                }

                bool collapsed;
                if (showGroups && collapsedGroups.TryGetValue(groupKey, out collapsed) && collapsed)
                    continue;
                layout.Add(new LayoutEntry { Item = item, Top = top, Height = RowHeight });
                top += RowHeight;
            }
            contentHeight = top;
            contentWidth = 0;
            for (int i = 0; i < baseColumnWidths.Length; i++)
                contentWidth += baseColumnWidths[i];
            contentWidth = Math.Max(contentWidth, ContentViewport.Width);
            ClampOffsets();
            Invalidate();
        }

        private static string GetGroupKey(ListViewItem item)
        {
            if (item.Group == null)
                return "__ungrouped";
            if (!string.IsNullOrEmpty(item.Group.Name))
                return item.Group.Name;
            return item.Group.Header ?? "__ungrouped";
        }

        private static string GetGroupTitle(ListViewItem item)
        {
            if (item.Group == null || string.IsNullOrWhiteSpace(item.Group.Header))
                return "Otros resultados";
            return item.Group.Header;
        }

        private void DrawHeader(Graphics graphics)
        {
            Rectangle header = new Rectangle(0, 0, Math.Max(0, Width - ScrollBarSize), HeaderHeight);
            using (SolidBrush fill = new SolidBrush(headerBackground))
                graphics.FillRectangle(fill, header);
            int x = -horizontalOffset;
            Point mouse = PointToClient(MousePosition);
            int hoveredDivider = HitTestColumnDivider(mouse);
            using (Pen separator = new Pen(border))
            {
                for (int column = 0; column < 6; column++)
                {
                    int width = GetColumnWidth(column);
                    Rectangle textBounds = new Rectangle(x + 10, 0, Math.Max(0, width - 18), HeaderHeight);
                    TextRenderer.DrawText(graphics, headers[column], regularFont, textBounds, text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    graphics.DrawLine(separator, x + width - 1, 6, x + width - 1, HeaderHeight - 6);
                    x += width;
                }
                graphics.DrawLine(separator, 0, HeaderHeight - 1, header.Right, HeaderHeight - 1);
            }
            if (hoveredDivider >= 0 || resizingColumn >= 0)
            {
                int activeDivider = resizingColumn >= 0 ? resizingColumn : hoveredDivider;
                int dividerX = GetColumnRight(activeDivider) - horizontalOffset;
                using (Pen active = new Pen(accent, 1.5F))
                    graphics.DrawLine(active, dividerX - 1, 5, dividerX - 1, HeaderHeight - 5);
            }
            using (SolidBrush corner = new SolidBrush(headerBackground))
                graphics.FillRectangle(corner, Math.Max(0, Width - ScrollBarSize), 0, ScrollBarSize, HeaderHeight);
        }

        private void DrawEntries(Graphics graphics, Rectangle viewport)
        {
            for (int index = 0; index < layout.Count; index++)
            {
                LayoutEntry entry = layout[index];
                int screenTop = viewport.Top + entry.Top - verticalOffset;
                if (screenTop + entry.Height <= viewport.Top || screenTop >= viewport.Bottom)
                    continue;
                Rectangle rowBounds = new Rectangle(viewport.Left, screenTop, viewport.Width, entry.Height);
                if (entry.IsGroup)
                    DrawGroup(graphics, entry, rowBounds, index);
                else
                    DrawRow(graphics, entry, rowBounds, index);
            }
        }

        private void DrawGroup(Graphics graphics, LayoutEntry entry, Rectangle bounds, int entryIndex)
        {
            Color groupBackground = entryIndex == hoverEntry ? Color.FromArgb(37, 37, 37) : background;
            using (SolidBrush fill = new SolidBrush(groupBackground))
                graphics.FillRectangle(fill, bounds);
            bool collapsed;
            collapsedGroups.TryGetValue(entry.GroupKey, out collapsed);
            int centerX = bounds.Left + 14;
            int centerY = bounds.Top + bounds.Height / 2;
            using (Pen pen = new Pen(accent, 1.5F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (collapsed)
                {
                    graphics.DrawLine(pen, centerX - 3, centerY - 4, centerX + 1, centerY);
                    graphics.DrawLine(pen, centerX + 1, centerY, centerX - 3, centerY + 4);
                }
                else
                {
                    graphics.DrawLine(pen, centerX - 4, centerY - 2, centerX, centerY + 2);
                    graphics.DrawLine(pen, centerX, centerY + 2, centerX + 4, centerY - 2);
                }
            }

            Rectangle textBounds = new Rectangle(bounds.Left + 29, bounds.Top,
                Math.Max(0, bounds.Width - 41), bounds.Height);
            TextRenderer.DrawText(graphics, entry.GroupTitle, semiboldFont, textBounds, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void DrawRow(Graphics graphics, LayoutEntry entry, Rectangle bounds, int entryIndex)
        {
            int itemIndex = items.IndexOf(entry.Item);
            Color rowColor = itemIndex % 2 == 0 ? rowEven : rowOdd;
            if (!entry.Item.BackColor.IsEmpty &&
                entry.Item.BackColor.ToArgb() != SystemColors.Window.ToArgb() &&
                entry.Item.BackColor.ToArgb() != background.ToArgb())
                rowColor = Blend(rowColor, entry.Item.BackColor, 0.14F);
            if (entry.Item == selectedItem)
                rowColor = selectedBackground;
            else if (entryIndex == hoverEntry)
                rowColor = hoverBackground;
            using (SolidBrush fill = new SolidBrush(rowColor))
                graphics.FillRectangle(fill, bounds);
            using (Pen line = new Pen(Color.FromArgb(43, 43, 43)))
                graphics.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            if (entry.Item == selectedItem)
            {
                using (SolidBrush selected = new SolidBrush(accent))
                    FillRoundedRectangle(graphics, selected,
                        new Rectangle(bounds.Left + 1, bounds.Top + 7, 3, bounds.Height - 14), 2);
            }

            int x = -horizontalOffset;
            for (int column = 0; column < 6; column++)
            {
                int width = GetColumnWidth(column);
                if (x + width > 0 && x < ContentViewport.Right)
                    DrawCell(graphics, entry.Item, column, new Rectangle(x, bounds.Top, width, bounds.Height));
                x += width;
            }
        }

        private void DrawCell(Graphics graphics, ListViewItem item, int column, Rectangle bounds)
        {
            int leftPadding = 10;
            if (column == 0 && showCheckBoxes)
            {
                Rectangle check = new Rectangle(bounds.Left + 10, bounds.Top + (bounds.Height - 16) / 2, 16, 16);
                DrawCheckBox(graphics, check, item.Checked);
                leftPadding = 38;
            }
            Color cellText = column == 0 ? text : secondaryText;
            if (column == 5)
            {
                string state = item.SubItems.Count > column ? item.SubItems[column].Text : string.Empty;
                bool error = state.IndexOf("fall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    state.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
                using (SolidBrush dot = new SolidBrush(error ? danger : accent))
                    graphics.FillEllipse(dot, bounds.Left + 10, bounds.Top + (bounds.Height - 6) / 2, 6, 6);
                leftPadding = 26;
                cellText = item == selectedItem ? text : Color.FromArgb(190, 190, 190);
            }
            string value = item.SubItems.Count > column ? item.SubItems[column].Text : string.Empty;
            Rectangle textBounds = new Rectangle(bounds.Left + leftPadding, bounds.Top,
                Math.Max(0, bounds.Width - leftPadding - 7), bounds.Height);
            TextRenderer.DrawText(graphics, value, item.Font ?? regularFont, textBounds, cellText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void DrawEmptyState(Graphics graphics, Rectangle viewport)
        {
            int centerY = viewport.Top + viewport.Height / 2;
            int centerX = viewport.Left + viewport.Width / 2;
            Rectangle iconBounds = new Rectangle(centerX - 24, centerY - 80, 48, 48);
            using (SolidBrush iconFill = new SolidBrush(Color.FromArgb(39, 39, 39)))
                graphics.FillEllipse(iconFill, iconBounds);
            using (Pen iconBorder = new Pen(Color.FromArgb(72, 72, 72), 1.2F))
                graphics.DrawEllipse(iconBorder, iconBounds);
            Point[] checkPoints =
            {
                new Point(centerX - 13, centerY - 56),
                new Point(centerX - 4, centerY - 47),
                new Point(centerX + 14, centerY - 67)
            };
            using (Pen check = new Pen(accent, 3.2F))
            {
                check.StartCap = LineCap.Round;
                check.EndCap = LineCap.Round;
                check.LineJoin = LineJoin.Round;
                graphics.DrawLines(check, checkPoints);
            }
            Rectangle titleBounds = new Rectangle(viewport.Left + 24, centerY - 23,
                Math.Max(0, viewport.Width - 48), 38);
            TextRenderer.DrawText(graphics, emptyTitle, emptyTitleFont, titleBounds, secondaryText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            Rectangle subtitleBounds = new Rectangle(viewport.Left + 24, centerY + 15,
                Math.Max(0, viewport.Width - 48), 28);
            TextRenderer.DrawText(graphics, emptySubtitle, regularFont, subtitleBounds, muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private void DrawScrollBars(Graphics graphics)
        {
            Rectangle vertical = VerticalTrack;
            Rectangle horizontal = HorizontalTrack;
            using (SolidBrush track = new SolidBrush(scrollTrack))
            {
                graphics.FillRectangle(track, vertical);
                graphics.FillRectangle(track, horizontal);
                graphics.FillRectangle(track, Math.Max(0, Width - ScrollBarSize),
                    Math.Max(HeaderHeight, Height - ScrollBarSize), ScrollBarSize, ScrollBarSize);
            }
            Point mouse = PointToClient(MousePosition);
            Rectangle verticalThumb = GetVerticalThumb();
            Rectangle horizontalThumb = GetHorizontalThumb();
            using (SolidBrush thumb = new SolidBrush(verticalThumb.Contains(mouse) || draggingVertical ? scrollThumbHover : scrollThumb))
                FillRoundedRectangle(graphics, thumb, verticalThumb, Math.Max(2, verticalThumb.Width / 2));
            using (SolidBrush thumb = new SolidBrush(horizontalThumb.Contains(mouse) || draggingHorizontal ? scrollThumbHover : scrollThumb))
                FillRoundedRectangle(graphics, thumb, horizontalThumb, Math.Max(2, horizontalThumb.Height / 2));
        }

        private Rectangle GetVerticalThumb()
        {
            Rectangle track = Rectangle.Inflate(VerticalTrack, -5, -5);
            if (track.Height <= 0)
                return Rectangle.Empty;
            int viewport = ContentViewport.Height;
            int thumbHeight = contentHeight <= viewport ? track.Height :
                Math.Max(MinimumThumb, (int)((long)track.Height * viewport / Math.Max(1, contentHeight)));
            thumbHeight = Math.Min(track.Height, thumbHeight);
            int range = Math.Max(0, contentHeight - viewport);
            int travel = Math.Max(0, track.Height - thumbHeight);
            int top = track.Top + (range == 0 ? 0 : (int)((long)travel * verticalOffset / range));
            return new Rectangle(track.Left, top, track.Width, thumbHeight);
        }

        private Rectangle GetHorizontalThumb()
        {
            Rectangle track = Rectangle.Inflate(HorizontalTrack, -6, -5);
            if (track.Width <= 0)
                return Rectangle.Empty;
            int viewport = ContentViewport.Width;
            int thumbWidth = contentWidth <= viewport ? track.Width :
                Math.Max(52, (int)((long)track.Width * viewport / Math.Max(1, contentWidth)));
            thumbWidth = Math.Min(track.Width, thumbWidth);
            int range = Math.Max(0, contentWidth - viewport);
            int travel = Math.Max(0, track.Width - thumbWidth);
            int left = track.Left + (range == 0 ? 0 : (int)((long)travel * horizontalOffset / range));
            return new Rectangle(left, track.Top, thumbWidth, track.Height);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            verticalOffset -= Math.Sign(e.Delta) * RowHeight * 3;
            ClampOffsets();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
            {
                int divider = HitTestColumnDivider(e.Location);
                if (divider >= 0)
                {
                    resizingColumn = divider;
                    resizeOrigin = e.X;
                    resizeWidthOrigin = GetColumnWidth(divider);
                    Capture = true;
                    Cursor = Cursors.VSplit;
                    return;
                }
            }
            Rectangle vThumb = GetVerticalThumb();
            Rectangle hThumb = GetHorizontalThumb();
            if (vThumb.Contains(e.Location))
            {
                draggingVertical = true;
                dragOrigin = e.Y;
                dragOffsetOrigin = verticalOffset;
                Capture = true;
                return;
            }
            if (hThumb.Contains(e.Location))
            {
                draggingHorizontal = true;
                dragOrigin = e.X;
                dragOffsetOrigin = horizontalOffset;
                Capture = true;
                return;
            }
            if (VerticalTrack.Contains(e.Location))
            {
                verticalOffset += e.Y < vThumb.Top ? -ContentViewport.Height : ContentViewport.Height;
                ClampOffsets();
                Invalidate();
                return;
            }
            if (HorizontalTrack.Contains(e.Location))
            {
                horizontalOffset += e.X < hThumb.Left ? -ContentViewport.Width : ContentViewport.Width;
                ClampOffsets();
                Invalidate();
                return;
            }
            if (e.Y < HeaderHeight)
            {
                int column = HitTestColumn(e.X);
                EventHandler<ColumnEventArgs> handler = ColumnClicked;
                if (column >= 0 && handler != null)
                    handler(this, new ColumnEventArgs(column));
                return;
            }

            int entryIndex = HitTestEntry(e.Location);
            if (entryIndex < 0)
                return;
            LayoutEntry entry = layout[entryIndex];
            if (entry.IsGroup)
            {
                bool collapsed;
                collapsedGroups.TryGetValue(entry.GroupKey, out collapsed);
                collapsedGroups[entry.GroupKey] = !collapsed;
                RebuildLayout();
                return;
            }

            SetSelectedItem(entry.Item, true);
            if (showCheckBoxes)
            {
                Rectangle check = GetCheckBoxBounds(entry);
                check.Inflate(4, 4);
                if (check.Contains(e.Location))
                {
                    entry.Item.Checked = !entry.Item.Checked;
                    EventHandler<ItemEventArgs> handler = ItemCheckedChanged;
                    if (handler != null)
                        handler(this, new ItemEventArgs(entry.Item));
                    Invalidate();
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (resizingColumn >= 0)
            {
                baseColumnWidths[resizingColumn] = Math.Max(minimumColumnWidths[resizingColumn],
                    resizeWidthOrigin + e.X - resizeOrigin);
                ClampOffsets();
                Cursor = Cursors.VSplit;
                Invalidate();
                return;
            }
            if (draggingVertical)
            {
                Rectangle thumb = GetVerticalThumb();
                Rectangle track = Rectangle.Inflate(VerticalTrack, -5, -5);
                int travel = Math.Max(1, track.Height - thumb.Height);
                int range = Math.Max(0, contentHeight - ContentViewport.Height);
                verticalOffset = dragOffsetOrigin + (int)((long)(e.Y - dragOrigin) * range / travel);
                ClampOffsets();
                Invalidate();
                return;
            }
            if (draggingHorizontal)
            {
                Rectangle thumb = GetHorizontalThumb();
                Rectangle track = Rectangle.Inflate(HorizontalTrack, -6, -5);
                int travel = Math.Max(1, track.Width - thumb.Width);
                int range = Math.Max(0, contentWidth - ContentViewport.Width);
                horizontalOffset = dragOffsetOrigin + (int)((long)(e.X - dragOrigin) * range / travel);
                ClampOffsets();
                Invalidate();
                return;
            }
            int nextHover = HitTestEntry(e.Location);
            if (hoverEntry != nextHover)
            {
                hoverEntry = nextHover;
                Invalidate();
            }
            bool overScrollThumb = GetVerticalThumb().Contains(e.Location) || GetHorizontalThumb().Contains(e.Location);
            bool overGroup = nextHover >= 0 && layout[nextHover].IsGroup;
            bool overColumnDivider = HitTestColumnDivider(e.Location) >= 0;
            Cursor = overColumnDivider ? Cursors.VSplit :
                (overScrollThumb || overGroup ? Cursors.Hand : Cursors.Default);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            draggingVertical = false;
            draggingHorizontal = false;
            resizingColumn = -1;
            Capture = false;
            Cursor = HitTestColumnDivider(e.Location) >= 0 ? Cursors.VSplit : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!draggingVertical && !draggingHorizontal && resizingColumn < 0)
            {
                hoverEntry = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && selectedItem != null && showCheckBoxes)
            {
                selectedItem.Checked = !selectedItem.Checked;
                EventHandler<ItemEventArgs> checkedHandler = ItemCheckedChanged;
                if (checkedHandler != null)
                    checkedHandler(this, new ItemEventArgs(selectedItem));
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down)
                return;
            int current = selectedItem == null ? -1 : items.IndexOf(selectedItem);
            int next = e.KeyCode == Keys.Up ? Math.Max(0, current - 1) : Math.Min(items.Count - 1, current + 1);
            if (next >= 0 && next < items.Count)
            {
                SetSelectedItem(items[next], true);
                EnsureSelectedVisible();
                e.Handled = true;
            }
        }

        private void SetSelectedItem(ListViewItem item, bool notify)
        {
            if (ReferenceEquals(selectedItem, item))
                return;
            selectedItem = item;
            Invalidate();
            EventHandler<ItemEventArgs> handler = SelectedItemChanged;
            if (notify && handler != null)
                handler(this, new ItemEventArgs(item));
        }

        private void EnsureSelectedVisible()
        {
            if (selectedItem == null)
                return;
            foreach (LayoutEntry entry in layout)
            {
                if (!entry.IsGroup && ReferenceEquals(entry.Item, selectedItem))
                {
                    if (entry.Top < verticalOffset)
                        verticalOffset = entry.Top;
                    else if (entry.Top + entry.Height > verticalOffset + ContentViewport.Height)
                        verticalOffset = entry.Top + entry.Height - ContentViewport.Height;
                    ClampOffsets();
                    Invalidate();
                    return;
                }
            }
        }

        private int HitTestEntry(Point point)
        {
            if (!ContentViewport.Contains(point))
                return -1;
            int contentY = point.Y - HeaderHeight + verticalOffset;
            for (int i = 0; i < layout.Count; i++)
            {
                LayoutEntry entry = layout[i];
                if (contentY >= entry.Top && contentY < entry.Top + entry.Height)
                    return i;
            }
            return -1;
        }

        private int HitTestColumn(int screenX)
        {
            int x = screenX + horizontalOffset;
            int left = 0;
            for (int column = 0; column < 6; column++)
            {
                int width = GetColumnWidth(column);
                if (x >= left && x < left + width)
                    return column;
                left += width;
            }
            return -1;
        }

        private int HitTestColumnDivider(Point point)
        {
            if (point.Y < 0 || point.Y >= HeaderHeight || point.X < 0 || point.X >= ContentViewport.Right)
                return -1;
            for (int column = 0; column < 6; column++)
            {
                int dividerX = GetColumnRight(column) - horizontalOffset;
                if (Math.Abs(point.X - dividerX) <= ColumnDividerTolerance)
                    return column;
            }
            return -1;
        }

        private int GetColumnRight(int column)
        {
            int right = 0;
            for (int index = 0; index <= column && index < 6; index++)
                right += GetColumnWidth(index);
            return right;
        }

        private Rectangle GetCellBounds(LayoutEntry entry, int column)
        {
            int left = -horizontalOffset;
            for (int i = 0; i < column; i++)
                left += GetColumnWidth(i);
            int top = HeaderHeight + entry.Top - verticalOffset;
            return new Rectangle(left, top, GetColumnWidth(column), entry.Height);
        }

        private Rectangle GetCheckBoxBounds(LayoutEntry entry)
        {
            Rectangle cell = GetCellBounds(entry, 0);
            return new Rectangle(cell.Left + 10, cell.Top + (cell.Height - 16) / 2, 16, 16);
        }

        private int GetColumnWidth(int column)
        {
            if (column != 5)
                return baseColumnWidths[column];
            int fixedWidth = 0;
            for (int i = 0; i < 5; i++)
                fixedWidth += baseColumnWidths[i];
            return Math.Max(baseColumnWidths[5], ContentViewport.Width - fixedWidth);
        }

        private void ClampOffsets()
        {
            int fixedWidth = 0;
            for (int i = 0; i < 5; i++)
                fixedWidth += baseColumnWidths[i];
            contentWidth = Math.Max(fixedWidth + baseColumnWidths[5], ContentViewport.Width);
            verticalOffset = Math.Max(0, Math.Min(verticalOffset,
                Math.Max(0, contentHeight - ContentViewport.Height)));
            horizontalOffset = Math.Max(0, Math.Min(horizontalOffset,
                Math.Max(0, contentWidth - ContentViewport.Width)));
        }

        private static void DrawCheckBox(Graphics graphics, Rectangle bounds, bool isChecked)
        {
            using (GraphicsPath path = RoundedPath(bounds, 4))
            {
                using (SolidBrush fill = new SolidBrush(isChecked ? Color.FromArgb(224, 226, 230) : Color.FromArgb(35, 35, 35)))
                    graphics.FillPath(fill, path);
                using (Pen outline = new Pen(isChecked ? Color.FromArgb(224, 226, 230) : Color.FromArgb(112, 112, 112)))
                    graphics.DrawPath(outline, path);
            }
            if (!isChecked)
                return;
            Point[] mark =
            {
                new Point(bounds.Left + 4, bounds.Top + 8),
                new Point(bounds.Left + 7, bounds.Top + 11),
                new Point(bounds.Left + 12, bounds.Top + 5)
            };
            using (Pen check = new Pen(Color.FromArgb(28, 28, 28), 1.8F))
            {
                check.StartCap = LineCap.Round;
                check.EndCap = LineCap.Round;
                check.LineJoin = LineJoin.Round;
                graphics.DrawLines(check, mark);
            }
        }

        private static Color Blend(Color baseColor, Color tint, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            float baseAmount = 1F - amount;
            return Color.FromArgb(
                (int)(baseColor.R * baseAmount + tint.R * amount),
                (int)(baseColor.G * baseAmount + tint.G * amount),
                (int)(baseColor.B * baseAmount + tint.B * amount));
        }

        private static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            using (GraphicsPath path = RoundedPath(bounds, radius))
                graphics.FillPath(brush, path);
        }

        private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2));
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
