using System;
using System.Drawing;
using System.Windows.Forms;

namespace wumgr
{
    internal sealed class PackageUpdateErrorDialog : Form
    {
        private static readonly Color DialogBackground = Color.FromArgb(31, 31, 31);
        private static readonly Color DialogSurface = Color.FromArgb(19, 19, 19);
        private static readonly Color DialogBorder = Color.FromArgb(68, 68, 68);
        private static readonly Color DialogText = Color.FromArgb(246, 246, 246);
        private static readonly Color DialogMuted = Color.FromArgb(188, 188, 188);
        private static readonly Color DialogButton = Color.FromArgb(88, 88, 88);
        private static readonly Color DialogButtonHover = Color.FromArgb(108, 108, 108);

        private readonly string diagnostic;
        private readonly RichTextBox diagnosticBox;

        public PackageUpdateErrorDialog(
            PackageUpdateInfo package, WinGetOperationResult result)
        {
            if (package == null)
                throw new ArgumentNullException("package");
            if (result == null)
                throw new ArgumentNullException("result");

            diagnostic = result.DiagnosticOutput ?? string.Empty;
            Text = "Error al actualizar " + package.Name;
            AccessibleName = Text;
            BackColor = DialogBorder;
            ForeColor = DialogText;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(830, 590);
            MinimumSize = new Size(700, 500);
            Padding = new Padding(1);
            KeyPreview = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = new Padding(20, 12, 20, 14);
            root.BackColor = DialogBackground;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

            root.Controls.Add(BuildTitleBar(package.Name), 0, 0);
            root.Controls.Add(BuildSummary(package, result), 0, 1);

            diagnosticBox = new RichTextBox();
            diagnosticBox.Dock = DockStyle.Fill;
            diagnosticBox.Margin = new Padding(0, 6, 0, 8);
            diagnosticBox.Padding = new Padding(8);
            diagnosticBox.BorderStyle = BorderStyle.None;
            diagnosticBox.BackColor = DialogSurface;
            diagnosticBox.ForeColor = Color.FromArgb(208, 208, 208);
            diagnosticBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            diagnosticBox.ReadOnly = true;
            diagnosticBox.WordWrap = false;
            diagnosticBox.ScrollBars = RichTextBoxScrollBars.Both;
            diagnosticBox.DetectUrls = false;
            diagnosticBox.Text = diagnostic;
            diagnosticBox.AccessibleName = "Diagnóstico completo de WinGet y del instalador";
            root.Controls.Add(diagnosticBox, 0, 2);
            root.Controls.Add(BuildButtons(), 0, 3);

            Controls.Add(root);
            CancelButton = FindCloseButton(root);
            Shown += delegate
            {
                diagnosticBox.SelectionStart = 0;
                diagnosticBox.SelectionLength = 0;
                diagnosticBox.ScrollToCaret();
            };
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
        }

        private Control BuildTitleBar(string packageName)
        {
            TableLayoutPanel titleBar = new TableLayoutPanel();
            titleBar.Dock = DockStyle.Fill;
            titleBar.Margin = Padding.Empty;
            titleBar.Padding = Padding.Empty;
            titleBar.BackColor = DialogBackground;
            titleBar.ColumnCount = 2;
            titleBar.RowCount = 1;
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));

            Label title = new Label();
            title.Dock = DockStyle.Fill;
            title.Margin = Padding.Empty;
            title.Text = "No se pudo actualizar " + packageName;
            title.ForeColor = DialogText;
            title.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point);
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.AutoEllipsis = true;

            Button close = new Button();
            close.Name = "dialogTitleCloseButton";
            close.Dock = DockStyle.Fill;
            close.Margin = new Padding(4, 4, 0, 8);
            close.Text = "×";
            close.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            close.ForeColor = DialogMuted;
            close.BackColor = DialogBackground;
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(72, 42, 42);
            close.Cursor = Cursors.Hand;
            close.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            titleBar.Controls.Add(title, 0, 0);
            titleBar.Controls.Add(close, 1, 0);
            return titleBar;
        }

        private Control BuildSummary(PackageUpdateInfo package, WinGetOperationResult result)
        {
            TableLayoutPanel summary = new TableLayoutPanel();
            summary.Dock = DockStyle.Fill;
            summary.Margin = Padding.Empty;
            summary.Padding = Padding.Empty;
            summary.BackColor = DialogBackground;
            summary.ColumnCount = 1;
            summary.RowCount = 2;
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            string reasonText = string.IsNullOrWhiteSpace(result.FailureReason)
                ? "El instalador del paquete terminó con un error."
                : result.FailureReason;
            Label reason = new Label();
            reason.Dock = DockStyle.Fill;
            reason.Margin = Padding.Empty;
            reason.Text = reasonText;
            reason.ForeColor = DialogText;
            reason.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            reason.TextAlign = ContentAlignment.MiddleLeft;
            reason.AutoEllipsis = true;

            Label hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.Margin = Padding.Empty;
            string code = "0x" + unchecked((uint)result.ExitCode).ToString("X8");
            hint.Text = "Código de WinGet " + code + ": "
                + (string.IsNullOrWhiteSpace(result.ErrorCodeExplanation)
                    ? "sin interpretación disponible."
                    : result.ErrorCodeExplanation)
                + Environment.NewLine
                + "La operación se ejecutó como administrador. El diagnóstico contiene toda la evidencia disponible.";
            hint.ForeColor = DialogMuted;
            hint.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.AutoEllipsis = true;

            summary.Controls.Add(reason, 0, 0);
            summary.Controls.Add(hint, 0, 1);
            return summary;
        }

        private Control BuildButtons()
        {
            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = Padding.Empty;
            buttons.Padding = new Padding(0, 10, 0, 0);
            buttons.BackColor = DialogBackground;
            buttons.ColumnCount = 3;
            buttons.RowCount = 1;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));

            Button retry = CreateDialogButton("Reintentar");
            retry.Name = "dialogRetryButton";
            retry.Margin = new Padding(0, 0, 6, 0);
            retry.Click += delegate
            {
                DialogResult = DialogResult.Retry;
                Close();
            };

            Button copy = CreateDialogButton("Copiar diagnóstico");
            copy.Margin = new Padding(0, 0, 6, 0);
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(diagnostic.Length == 0
                        ? "No hay diagnóstico disponible."
                        : diagnostic);
                    copy.Text = "Copiado";
                }
                catch (Exception exception)
                {
                    MessageBox.Show("No se pudo copiar el diagnóstico: " + exception.Message,
                        Program.mName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Button close = CreateDialogButton("Cerrar");
            close.Name = "dialogCloseButton";
            close.Margin = Padding.Empty;
            close.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            buttons.Controls.Add(retry, 0, 0);
            buttons.Controls.Add(copy, 1, 0);
            buttons.Controls.Add(close, 2, 0);
            return buttons;
        }

        private static Button CreateDialogButton(string text)
        {
            Button button = new Button();
            button.Dock = DockStyle.Fill;
            button.Text = text;
            button.BackColor = DialogButton;
            button.ForeColor = DialogText;
            button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = DialogButtonHover;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static Button FindCloseButton(Control root)
        {
            foreach (Control control in root.Controls)
            {
                Button button = control as Button;
                if (button != null && button.Name == "dialogCloseButton")
                    return button;
                Button nested = FindCloseButton(control);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(DialogBorder))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
    }
}
