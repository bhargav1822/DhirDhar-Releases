using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DhirDhar.Application.Licensing.Models;

namespace DhirDhar.LicenseGenerator;

public sealed class MainForm : Form
{
    private readonly LicenseHistoryService _historyService = new();

    // Dark Theme Palette
    private static readonly Color BgDark = Color.FromArgb(11, 15, 25);            // #0B0F19 Window background
    private static readonly Color PanelDark = Color.FromArgb(17, 24, 39);          // #111827 Card background
    private static readonly Color SubPanelDark = Color.FromArgb(15, 23, 42);       // #0F172A Inner card background
    private static readonly Color InputDark = Color.FromArgb(15, 23, 42);          // #0F172A Input background
    private static readonly Color BorderSlate = Color.FromArgb(51, 65, 85);        // #334155 Border
    private static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);     // #F8FAFC
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);       // #94A3B8
    private static readonly Color TextHighlight = Color.FromArgb(56, 189, 248);    // #38BDF8 Sky blue
    private static readonly Color GreenAccent = Color.FromArgb(16, 185, 129);      // #10B981 Emerald
    private static readonly Color GreenDark = Color.FromArgb(6, 78, 59);           // #064E3B
    private static readonly Color GreenButton = Color.FromArgb(5, 150, 105);       // #059669
    private static readonly Color GreenButtonHover = Color.FromArgb(4, 120, 87);   // #047857
    private static readonly Color BlueButton = Color.FromArgb(37, 99, 235);        // #2563EB
    private static readonly Color BlueButtonHover = Color.FromArgb(29, 78, 216);   // #1D4ED8
    private static readonly Color AmberAccent = Color.FromArgb(245, 158, 11);      // #F59E0B
    private static readonly Color AmberDark = Color.FromArgb(69, 26, 3);           // #451A03
    private static readonly Color TabActiveBg = Color.FromArgb(30, 41, 59);        // #1E293B
    private static readonly Color TabInactiveText = Color.FromArgb(148, 163, 184); // #94A3B8

    // Navigation Tabs
    private int _selectedTabIndex = 0;
    private readonly List<Button> _tabButtons = new();
    private readonly List<Panel> _tabPanels = new();

    // Tab 1: Generate Controls
    private TextBox _txtCustomerName = null!;
    private TextBox _txtCustomerEmail = null!;
    private TextBox _txtLicenseId = null!;
    private Button _btnRegenId = null!;
    private ComboBox _cmbLicenseType = null!;
    private TextBox _txtPreviousLicenseId = null!;
    private Label _lblPreviousLicId = null!;
    private TextBox _txtHardwareId = null!;
    private DateTimePicker _dtpIssueDate = null!;
    private TextBox _txtExpiryDateDisplay = null!;
    private Button _btnGenerateAnnual = null!;
    private Panel _pnlGeneratedResult = null!;
    private TextBox _txtGeneratedSerialKey = null!;
    private Button _btnCopyGeneratedKey = null!;
    private Label _lblCopyGeneratedFeedback = null!;
    private Label _lblResultSummary = null!;

    // Tab 2: Verify Controls
    private TextBox _txtVerifyInputKey = null!;
    private Button _btnVerifyKey = null!;
    private Button _btnClearVerify = null!;
    private Panel _pnlVerifyResult = null!;
    private Label _lblVerifyStatusBadge = null!;
    private DataGridView _dgvVerifyDetails = null!;

    // Tab 3: History Controls
    private DataGridView _dgvHistory = null!;
    private Button _btnRefreshHistory = null!;
    private Button _btnCopySelectedHistoryKey = null!;
    private Label _lblHistoryCount = null!;

    // Tab 4: Key Management Controls
    private TextBox _txtPublicKeyPem = null!;
    private Button _btnCopyPublicKey = null!;
    private Label _lblKeyStatus = null!;

    public MainForm()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "DhirDhar License Generator (Developer / Administrator Tool)";
        Size = new Size(1220, 880);
        MinimumSize = new Size(1120, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        LoadApplicationIcon();

        // 1. Header Panel (Fixed Height, Dock Top)
        var pnlHeader = BuildHeaderPanel();
        Controls.Add(pnlHeader);

        // 2. Tab Navigation Strip (Dock Top below Header)
        var pnlTabs = BuildTabStrip();
        Controls.Add(pnlTabs);

        // 3. Main Content Container Panel (Dock Fill)
        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            Padding = new Padding(24, 16, 24, 20)
        };
        Controls.Add(pnlContent);

        // Build 4 Tab Pages
        var pageGenerate = BuildGenerateTab();
        var pageVerify = BuildVerifyTab();
        var pageHistory = BuildHistoryTab();
        var pageKeyMgmt = BuildKeyManagementTab();

        _tabPanels.Add(pageGenerate);
        _tabPanels.Add(pageVerify);
        _tabPanels.Add(pageHistory);
        _tabPanels.Add(pageKeyMgmt);

        foreach (var p in _tabPanels)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            pnlContent.Controls.Add(p);
        }

        // Set Tab 0 Active by Default
        SelectTab(0);
    }

    #region 1. Header & Navigation Bar
    private Panel BuildHeaderPanel()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = PanelDark,
            Padding = new Padding(28, 14, 28, 14)
        };
        pnl.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawLine(pen, 0, pnl.Height - 1, pnl.Width, pnl.Height - 1);
        };

        // Left Container
        var pnlLeft = new Panel
        {
            Location = new Point(28, 12),
            Size = new Size(700, 68),
            BackColor = Color.Transparent
        };

        var lblBrand = new Label
        {
            Text = "DhirDhar",
            Font = new Font("Segoe UI", 19f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(0, 0),
            AutoSize = true
        };
        pnlLeft.Controls.Add(lblBrand);

        var lblAnnualBadge = new Label
        {
            Text = "ANNUAL LICENSE GENERATOR",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = GreenButton,
            Location = new Point(148, 6),
            Size = new Size(196, 24),
            TextAlign = ContentAlignment.MiddleCenter
        };
        pnlLeft.Controls.Add(lblAnnualBadge);

        var lblSubtitle = new Label
        {
            Text = "ECDSA P-256 Asymmetric Cryptographic License Signing & Verification",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = TextMuted,
            Location = new Point(2, 38),
            AutoSize = true
        };
        pnlLeft.Controls.Add(lblSubtitle);
        pnl.Controls.Add(pnlLeft);

        // Right Container: Confidential Badge
        var pnlConfidential = new Panel
        {
            Size = new Size(230, 38),
            Location = new Point(pnl.Width - 260, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = AmberDark
        };
        pnlConfidential.Paint += (s, e) =>
        {
            using var pen = new Pen(AmberAccent, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlConfidential.Width - 1, pnlConfidential.Height - 1);
        };

        var lblConfidential = new Label
        {
            Text = "🔒 Confidential Developer Tool",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = AmberAccent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        pnlConfidential.Controls.Add(lblConfidential);
        pnl.Controls.Add(pnlConfidential);

        return pnl;
    }

    private Panel BuildTabStrip()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = PanelDark,
            Padding = new Padding(28, 0, 28, 0)
        };
        pnl.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawLine(pen, 0, pnl.Height - 1, pnl.Width, pnl.Height - 1);
        };

        var tabNames = new[]
        {
            "⚡ Generate Annual License",
            "🔍 Verify Serial Key",
            "📜 License History",
            "🔑 Key Management"
        };

        int x = 28;
        for (int i = 0; i < tabNames.Length; i++)
        {
            int index = i;
            var btn = new Button
            {
                Text = tabNames[i],
                Location = new Point(x, 4),
                Size = new Size(210, 40),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TabInactiveText,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => SelectTab(index);

            _tabButtons.Add(btn);
            pnl.Controls.Add(btn);
            x += 218;
        }

        return pnl;
    }

    private void SelectTab(int index)
    {
        _selectedTabIndex = index;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool active = i == index;
            _tabButtons[i].BackColor = active ? TabActiveBg : Color.Transparent;
            _tabButtons[i].ForeColor = active ? GreenAccent : TabInactiveText;
            _tabPanels[i].Visible = active;
        }

        if (index == 2)
        {
            LoadHistoryGrid();
        }
    }
    #endregion

    #region 2. Tab 1: Generate Annual License
    private Panel BuildGenerateTab()
    {
        var scrollPanel = new Panel
        {
            AutoScroll = true,
            BackColor = BgDark,
            Dock = DockStyle.Fill
        };

        var card = new Panel
        {
            Location = new Point(0, 0),
            Width = 1140,
            Height = 760,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = PanelDark,
            Padding = new Padding(28, 24, 28, 28)
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        scrollPanel.Controls.Add(card);

        // Main Layout: TableLayoutPanel for clean 2-Column Grid
        var tlpTwoCols = new TableLayoutPanel
        {
            Location = new Point(28, 20),
            Size = new Size(1084, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        tlpTwoCols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        tlpTwoCols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        // LEFT COLUMN CONTAINER
        var pnlLeftCol = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };

        int ly = 0;
        // 1. Customer Name
        var lblCustName = CreateLabel("Customer / Business Name *", 0, ly);
        pnlLeftCol.Controls.Add(lblCustName);
        _txtCustomerName = CreateTextBox(0, ly + 22, 510, "e.g. Ramesh Patel / Patel Traders");
        _txtCustomerName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlLeftCol.Controls.Add(_txtCustomerName);
        ly += 60;

        // 2. Customer Email
        var lblCustEmail = CreateLabel("Customer Email (Optional)", 0, ly);
        pnlLeftCol.Controls.Add(lblCustEmail);
        _txtCustomerEmail = CreateTextBox(0, ly + 22, 510, "customer@example.com");
        _txtCustomerEmail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlLeftCol.Controls.Add(_txtCustomerEmail);
        ly += 60;

        // 3. Unique License ID Row (TextBox + Regenerate Button)
        var lblLicId = CreateLabel("Unique License ID", 0, ly);
        pnlLeftCol.Controls.Add(lblLicId);

        var pnlLicIdRow = new Panel { Location = new Point(0, ly + 22), Size = new Size(510, 32), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _txtLicenseId = new TextBox
        {
            Location = new Point(0, 0),
            Size = new Size(350, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 10.5f, FontStyle.Bold),
            BackColor = InputDark,
            ForeColor = TextHighlight,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true
        };
        pnlLicIdRow.Controls.Add(_txtLicenseId);

        _btnRegenId = new Button
        {
            Text = "🔄 Regenerate ID",
            Location = new Point(360, 0),
            Size = new Size(150, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = SubPanelDark,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnRegenId.FlatAppearance.BorderColor = BorderSlate;
        _btnRegenId.Click += (s, e) => _txtLicenseId.Text = LicenseSigner.GenerateLicenseId();
        pnlLicIdRow.Controls.Add(_btnRegenId);
        pnlLeftCol.Controls.Add(pnlLicIdRow);
        ly += 60;

        // Initial ID
        _txtLicenseId.Text = LicenseSigner.GenerateLicenseId();

        // 4. License Operation Mode
        var lblOpMode = CreateLabel("License Operation Mode", 0, ly);
        pnlLeftCol.Controls.Add(lblOpMode);

        _cmbLicenseType = new ComboBox
        {
            Location = new Point(0, ly + 22),
            Size = new Size(510, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10f),
            BackColor = InputDark,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat
        };
        _cmbLicenseType.Items.AddRange(new object[] { "New Annual License (365 Days)", "License Renewal (365 Days)" });
        _cmbLicenseType.SelectedIndex = 0;
        _cmbLicenseType.SelectedIndexChanged += OnGenLicenseTypeChanged;
        pnlLeftCol.Controls.Add(_cmbLicenseType);

        tlpTwoCols.Controls.Add(pnlLeftCol, 0, 0);

        // RIGHT COLUMN CONTAINER
        var pnlRightCol = new Panel { Dock = DockStyle.Fill, Margin = new Padding(16, 0, 0, 0) };

        int ry = 0;
        // 1. Product & Edition
        var lblProduct = CreateLabel("Product & Edition", 0, ry);
        pnlRightCol.Controls.Add(lblProduct);
        var txtProduct = CreateTextBox(0, ry + 22, 510, string.Empty);
        txtProduct.Text = "DhirDhar — Annual (365 Days)";
        txtProduct.ReadOnly = true;
        txtProduct.ForeColor = TextHighlight;
        txtProduct.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlRightCol.Controls.Add(txtProduct);
        ry += 60;

        // 2. Dates Row (Issue Date & Expiry Date)
        var tlpDates = new TableLayoutPanel
        {
            Location = new Point(0, ry),
            Size = new Size(510, 56),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        tlpDates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        tlpDates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var lblIssue = CreateLabel("Issue Date", 0, 0);
        tlpDates.Controls.Add(lblIssue, 0, 0);

        var lblExpiry = CreateLabel("Expiry Date (+365 Days)", 0, 0);
        tlpDates.Controls.Add(lblExpiry, 1, 0);

        _dtpIssueDate = new DateTimePicker
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f),
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd-MMM-yyyy",
            Value = DateTime.UtcNow.Date,
            BackColor = InputDark,
            ForeColor = TextPrimary,
            Margin = new Padding(0, 4, 8, 0)
        };
        _dtpIssueDate.ValueChanged += (s, e) => UpdateExpiryDateDisplay();
        tlpDates.Controls.Add(_dtpIssueDate, 0, 1);

        _txtExpiryDateDisplay = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ReadOnly = true,
            BackColor = InputDark,
            ForeColor = GreenAccent,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(8, 4, 0, 0)
        };
        tlpDates.Controls.Add(_txtExpiryDateDisplay, 1, 1);
        UpdateExpiryDateDisplay();

        pnlRightCol.Controls.Add(tlpDates);
        ry += 60;

        // 3. Device Limit / Binding
        var lblDevLimit = CreateLabel("Device Limit", 0, ry);
        pnlRightCol.Controls.Add(lblDevLimit);
        var txtDevLimit = CreateTextBox(0, ry + 22, 510, string.Empty);
        txtDevLimit.Text = "1 Windows PC (Hardware Bound)";
        txtDevLimit.ReadOnly = true;
        txtDevLimit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlRightCol.Controls.Add(txtDevLimit);
        ry += 60;

        // 4. Previous License ID (Required for Renewals)
        _lblPreviousLicId = CreateLabel("Previous License ID (Required for Renewals)", 0, ry);
        _lblPreviousLicId.ForeColor = TextMuted;
        pnlRightCol.Controls.Add(_lblPreviousLicId);

        _txtPreviousLicenseId = CreateTextBox(0, ry + 22, 510, "Required for renewals (e.g. DD-20260817-XXXXXX)");
        _txtPreviousLicenseId.Enabled = false;
        _txtPreviousLicenseId.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlRightCol.Controls.Add(_txtPreviousLicenseId);

        tlpTwoCols.Controls.Add(pnlRightCol, 1, 0);
        card.Controls.Add(tlpTwoCols);

        int curY = 310;

        // Full Width Row: Hardware ID / Device Binding
        var lblHwBinding = CreateLabel("Hardware ID / Device Binding (Optional - Leave blank for unbound)", 28, curY);
        card.Controls.Add(lblHwBinding);

        _txtHardwareId = CreateTextBox(28, curY + 22, 1084, "e.g. DD-PC-A1B2C3D4E5F6 (Leave blank if customer PC hardware ID is not yet known)");
        _txtHardwareId.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(_txtHardwareId);

        curY += 66;

        // Full Width Generate Button
        _btnGenerateAnnual = new Button
        {
            Text = "Generate & Sign Customer Annual Serial Key",
            Location = new Point(28, curY),
            Size = new Size(1084, 46),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = GreenButton,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnGenerateAnnual.FlatAppearance.BorderSize = 0;
        _btnGenerateAnnual.Click += OnGenerateAnnualClick;
        card.Controls.Add(_btnGenerateAnnual);

        curY += 60;

        // Generated Serial Key Result Section Card
        _pnlGeneratedResult = new Panel
        {
            Location = new Point(28, curY),
            Size = new Size(1084, 220),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = SubPanelDark,
            Visible = false
        };
        _pnlGeneratedResult.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _pnlGeneratedResult.Width - 1, _pnlGeneratedResult.Height - 1);
        };

        var lblResHeader = new Label
        {
            Text = "Generated Customer Serial Key (25-Character Key):",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(18, 14),
            AutoSize = true
        };
        _pnlGeneratedResult.Controls.Add(lblResHeader);

        _btnCopyGeneratedKey = new Button
        {
            Text = "📋 Copy Serial Key",
            Location = new Point(_pnlGeneratedResult.Width - 190, 10),
            Size = new Size(172, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = BlueButton,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnCopyGeneratedKey.FlatAppearance.BorderSize = 0;
        _btnCopyGeneratedKey.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_txtGeneratedSerialKey.Text))
            {
                Clipboard.SetText(_txtGeneratedSerialKey.Text.Trim());
                _lblCopyGeneratedFeedback.Visible = true;
            }
        };
        _pnlGeneratedResult.Controls.Add(_btnCopyGeneratedKey);

        _lblCopyGeneratedFeedback = new Label
        {
            Text = "✓ Copied to clipboard!",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = GreenAccent,
            Location = new Point(_pnlGeneratedResult.Width - 365, 17),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true,
            Visible = false
        };
        _pnlGeneratedResult.Controls.Add(_lblCopyGeneratedFeedback);

        _txtGeneratedSerialKey = new TextBox
        {
            Location = new Point(18, 52),
            Size = new Size(1048, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            Font = new Font("Consolas", 14f, FontStyle.Bold),
            BackColor = Color.FromArgb(10, 14, 22),
            ForeColor = TextHighlight,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center
        };
        _pnlGeneratedResult.Controls.Add(_txtGeneratedSerialKey);

        _lblResultSummary = new Label
        {
            Text = "License Summary: -",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = TextMuted,
            Location = new Point(18, 100),
            Size = new Size(1048, 105),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _pnlGeneratedResult.Controls.Add(_lblResultSummary);

        card.Controls.Add(_pnlGeneratedResult);
        card.Height = curY + 240;

        return scrollPanel;
    }

    private void UpdateExpiryDateDisplay()
    {
        var exp = _dtpIssueDate.Value.Date.AddDays(365);
        _txtExpiryDateDisplay.Text = exp.ToString("dd-MMM-yyyy");
    }

    private void OnGenLicenseTypeChanged(object? sender, EventArgs e)
    {
        bool isRenewal = _cmbLicenseType.SelectedIndex == 1;
        _txtPreviousLicenseId.Enabled = isRenewal;
        _lblPreviousLicId.ForeColor = isRenewal ? TextPrimary : TextMuted;
        if (!isRenewal)
        {
            _txtPreviousLicenseId.Text = string.Empty;
        }
    }

    private void OnGenerateAnnualClick(object? sender, EventArgs e)
    {
        try
        {
            _lblCopyGeneratedFeedback.Visible = false;

            var customerName = _txtCustomerName.Text.Trim();
            if (string.IsNullOrWhiteSpace(customerName))
            {
                customerName = "Valued Customer";
            }

            var customerEmail = _txtCustomerEmail.Text.Trim();
            if (string.IsNullOrWhiteSpace(customerEmail))
            {
                customerEmail = "customer@dhirdhar.com";
            }

            var hardwareId = _txtHardwareId.Text.Trim();
            if (string.IsNullOrWhiteSpace(hardwareId))
            {
                hardwareId = null;
            }

            var issuedAt = _dtpIssueDate.Value.Date;
            var expiresAt = issuedAt.AddDays(365);
            bool isRenewal = _cmbLicenseType.SelectedIndex == 1;

            LicensePayload payload;
            string serialKey;

            if (isRenewal)
            {
                var prevId = _txtPreviousLicenseId.Text.Trim();
                if (string.IsNullOrWhiteSpace(prevId))
                {
                    MessageBox.Show(
                        "Please provide the Previous License ID for issuing a renewal.",
                        "Previous License ID Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _txtPreviousLicenseId.Focus();
                    return;
                }

                (payload, serialKey) = LicenseSigner.CreateUniqueRenewal(
                    previousLicenseId: prevId,
                    customerName: customerName,
                    customerEmail: customerEmail,
                    historyService: _historyService,
                    customIssuedAt: issuedAt,
                    customExpiresAt: expiresAt,
                    deviceBinding: hardwareId);
            }
            else
            {
                (payload, serialKey) = LicenseSigner.CreateUniqueLicense(
                    customerName: customerName,
                    customerEmail: customerEmail,
                    historyService: _historyService,
                    customIssuedAt: issuedAt,
                    customExpiresAt: expiresAt,
                    deviceBinding: hardwareId);
            }

            // Display Results
            _txtGeneratedSerialKey.Text = serialKey;
            _lblResultSummary.Text =
                $"• License ID: {payload.LicenseId}      • Issuance ID: {payload.IssuanceId}\n" +
                $"• Customer: {payload.CustomerName} ({payload.CustomerEmail})      • License Type: {(payload.Renewal ? "Renewal" : "Annual")}\n" +
                $"• Issue Date: {payload.IssuedAt:dd-MMM-yyyy}      • Expiry Date: {payload.ExpiresAt:dd-MMM-yyyy} (365 Days)\n" +
                $"• Hardware Binding: {(string.IsNullOrEmpty(payload.DeviceBinding) ? "Unbound (Any Windows PC)" : payload.DeviceBinding)}\n" +
                $"• Digital Signature: ✓ ECDSA P-256 SHA-256 Validated";

            _pnlGeneratedResult.Visible = true;

            // Generate next fresh License ID for next run
            _txtLicenseId.Text = LicenseSigner.GenerateLicenseId();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"License generation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    #endregion

    #region 3. Tab 2: Verify Serial Key
    private Panel BuildVerifyTab()
    {
        var scrollPanel = new Panel { AutoScroll = true, BackColor = BgDark, Dock = DockStyle.Fill };
        var card = new Panel
        {
            Location = new Point(0, 0),
            Width = 1140,
            Height = 720,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = PanelDark,
            Padding = new Padding(28, 24, 28, 28)
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        scrollPanel.Controls.Add(card);

        var lblHeader = CreateSectionHeader("🔍 Verify Customer Serial Key", 28, 16);
        card.Controls.Add(lblHeader);

        var lblInstructions = CreateLabel("Paste any generated serial key to decode, verify the digital signature, and inspect the canonical payload.", 28, 44);
        lblInstructions.ForeColor = TextMuted;
        card.Controls.Add(lblInstructions);

        _txtVerifyInputKey = new TextBox
        {
            Location = new Point(28, 72),
            Size = new Size(1084, 96),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular),
            BackColor = InputDark,
            ForeColor = TextHighlight,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Paste XXXXX-XXXXX-XXXXX-XXXXX-XXXXX serial key here"
        };
        card.Controls.Add(_txtVerifyInputKey);

        _btnVerifyKey = new Button
        {
            Text = "🔍 Verify Digital Signature & Payload",
            Location = new Point(28, 178),
            Size = new Size(300, 38),
            BackColor = BlueButton,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnVerifyKey.FlatAppearance.BorderSize = 0;
        _btnVerifyKey.Click += OnVerifyKeyClick;
        card.Controls.Add(_btnVerifyKey);

        _btnClearVerify = new Button
        {
            Text = "Clear",
            Location = new Point(338, 178),
            Size = new Size(100, 38),
            BackColor = SubPanelDark,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnClearVerify.FlatAppearance.BorderColor = BorderSlate;
        _btnClearVerify.Click += (s, e) =>
        {
            _txtVerifyInputKey.Text = string.Empty;
            _pnlVerifyResult.Visible = false;
        };
        card.Controls.Add(_btnClearVerify);

        // Verification Results Container
        _pnlVerifyResult = new Panel
        {
            Location = new Point(28, 230),
            Size = new Size(1084, 450),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = SubPanelDark,
            Visible = false
        };
        _pnlVerifyResult.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _pnlVerifyResult.Width - 1, _pnlVerifyResult.Height - 1);
        };

        _lblVerifyStatusBadge = new Label
        {
            Location = new Point(18, 14),
            Size = new Size(1048, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };
        _pnlVerifyResult.Controls.Add(_lblVerifyStatusBadge);

        _dgvVerifyDetails = new DataGridView
        {
            Location = new Point(18, 58),
            Size = new Size(1048, 372),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundColor = InputDark,
            ForeColor = TextPrimary,
            GridColor = BorderSlate,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };
        _dgvVerifyDetails.DefaultCellStyle.BackColor = InputDark;
        _dgvVerifyDetails.DefaultCellStyle.ForeColor = TextPrimary;
        _dgvVerifyDetails.DefaultCellStyle.SelectionBackColor = TabActiveBg;
        _dgvVerifyDetails.DefaultCellStyle.SelectionForeColor = TextHighlight;
        _dgvVerifyDetails.ColumnHeadersDefaultCellStyle.BackColor = PanelDark;
        _dgvVerifyDetails.ColumnHeadersDefaultCellStyle.ForeColor = TextHighlight;
        _dgvVerifyDetails.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _dgvVerifyDetails.EnableHeadersVisualStyles = false;

        _dgvVerifyDetails.Columns.Add("Property", "License Property");
        _dgvVerifyDetails.Columns.Add("Value", "Decoded Payload Value");
        _dgvVerifyDetails.Columns[0].Width = 260;

        _pnlVerifyResult.Controls.Add(_dgvVerifyDetails);
        card.Controls.Add(_pnlVerifyResult);

        return scrollPanel;
    }

    private void OnVerifyKeyClick(object? sender, EventArgs e)
    {
        var key = _txtVerifyInputKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("Please paste a serial key to verify.", "Serial Key Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool isValid = LicenseSigner.VerifySerialKey(key, LicenseSigner.DefaultPublicKeyPem, out var payload, out var errorMessage);
        _dgvVerifyDetails.Rows.Clear();

        if (isValid && payload != null)
        {
            _lblVerifyStatusBadge.Text = "✓ Digital Signature Valid — Authentic DhirDhar License";
            _lblVerifyStatusBadge.BackColor = GreenDark;
            _lblVerifyStatusBadge.ForeColor = GreenAccent;

            var daysRemaining = (int)(payload.ExpiresAt.Date - DateTime.UtcNow.Date).TotalDays;
            var status = daysRemaining < 0 ? "EXPIRED" : $"{daysRemaining} Days Remaining (Active)";

            _dgvVerifyDetails.Rows.Add("Product", payload.Product);
            _dgvVerifyDetails.Rows.Add("License ID", payload.LicenseId);
            _dgvVerifyDetails.Rows.Add("Issuance ID", payload.IssuanceId);
            _dgvVerifyDetails.Rows.Add("Customer Name", payload.CustomerName);
            _dgvVerifyDetails.Rows.Add("Customer Email", payload.CustomerEmail);
            _dgvVerifyDetails.Rows.Add("License Type", payload.Renewal ? "Renewal" : payload.LicenseType);
            _dgvVerifyDetails.Rows.Add("Issue Date", payload.IssuedAt.ToString("dd-MMM-yyyy"));
            _dgvVerifyDetails.Rows.Add("Expiry Date", payload.ExpiresAt.ToString("dd-MMM-yyyy"));
            _dgvVerifyDetails.Rows.Add("Status / Validity", status);
            _dgvVerifyDetails.Rows.Add("Device Limit", $"{payload.DeviceLimit} PC");
            _dgvVerifyDetails.Rows.Add("Bound Device ID", string.IsNullOrEmpty(payload.DeviceBinding) ? "Unbound (Any Windows PC)" : payload.DeviceBinding);
            _dgvVerifyDetails.Rows.Add("Previous License ID", string.IsNullOrEmpty(payload.PreviousLicenseId) ? "-" : payload.PreviousLicenseId);
            _dgvVerifyDetails.Rows.Add("Renewal Flag", payload.Renewal.ToString());
            _dgvVerifyDetails.Rows.Add("License Format Version", payload.LicenseVersion.ToString());
        }
        else
        {
            _lblVerifyStatusBadge.Text = $"✗ Verification Failed: {errorMessage}";
            _lblVerifyStatusBadge.BackColor = Color.FromArgb(69, 26, 26);
            _lblVerifyStatusBadge.ForeColor = Color.FromArgb(248, 113, 113);

            _dgvVerifyDetails.Rows.Add("Error Detail", errorMessage);
        }

        _pnlVerifyResult.Visible = true;
    }
    #endregion

    #region 4. Tab 3: License History
    private Panel BuildHistoryTab()
    {
        var pnl = new Panel { BackColor = BgDark, Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 24) };

        var lblTitle = CreateSectionHeader("📜 Local License Issuance History", 0, 0);
        pnl.Controls.Add(lblTitle);

        _lblHistoryCount = CreateLabel("0 total licenses recorded.", 0, 26);
        _lblHistoryCount.ForeColor = TextMuted;
        pnl.Controls.Add(_lblHistoryCount);

        _btnRefreshHistory = new Button
        {
            Text = "🔄 Refresh",
            Location = new Point(pnl.Width - 270, 4),
            Size = new Size(110, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = SubPanelDark,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnRefreshHistory.FlatAppearance.BorderColor = BorderSlate;
        _btnRefreshHistory.Click += (s, e) => LoadHistoryGrid();
        pnl.Controls.Add(_btnRefreshHistory);

        _btnCopySelectedHistoryKey = new Button
        {
            Text = "📋 Copy Key",
            Location = new Point(pnl.Width - 150, 4),
            Size = new Size(122, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = BlueButton,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnCopySelectedHistoryKey.FlatAppearance.BorderSize = 0;
        _btnCopySelectedHistoryKey.Click += OnCopySelectedHistoryKeyClick;
        pnl.Controls.Add(_btnCopySelectedHistoryKey);

        _dgvHistory = new DataGridView
        {
            Location = new Point(0, 52),
            Size = new Size(pnl.Width, pnl.Height - 68),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundColor = PanelDark,
            ForeColor = TextPrimary,
            GridColor = BorderSlate,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };
        _dgvHistory.DefaultCellStyle.BackColor = SubPanelDark;
        _dgvHistory.DefaultCellStyle.ForeColor = TextPrimary;
        _dgvHistory.DefaultCellStyle.SelectionBackColor = TabActiveBg;
        _dgvHistory.DefaultCellStyle.SelectionForeColor = TextHighlight;
        _dgvHistory.ColumnHeadersDefaultCellStyle.BackColor = PanelDark;
        _dgvHistory.ColumnHeadersDefaultCellStyle.ForeColor = TextHighlight;
        _dgvHistory.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _dgvHistory.EnableHeadersVisualStyles = false;

        _dgvHistory.Columns.Add("LicenseId", "License ID");
        _dgvHistory.Columns.Add("Type", "Type");
        _dgvHistory.Columns.Add("Customer", "Customer Name");
        _dgvHistory.Columns.Add("Issued", "Issue Date");
        _dgvHistory.Columns.Add("Expires", "Expiry Date");
        _dgvHistory.Columns.Add("PrevId", "Previous Lic ID");
        _dgvHistory.Columns.Add("SerialKey", "Serial Key");

        _dgvHistory.Columns[0].Width = 150;
        _dgvHistory.Columns[1].Width = 90;
        _dgvHistory.Columns[2].Width = 180;
        _dgvHistory.Columns[3].Width = 110;
        _dgvHistory.Columns[4].Width = 110;
        _dgvHistory.Columns[5].Width = 150;

        pnl.Controls.Add(_dgvHistory);

        return pnl;
    }

    private void LoadHistoryGrid()
    {
        _dgvHistory.Rows.Clear();
        var records = _historyService.GetAllRecords();
        _lblHistoryCount.Text = $"{records.Count} total licenses recorded in local history.";

        foreach (var r in records.Reverse())
        {
            var type = r.IsRenewal ? "Renewal" : r.Edition;
            var prevId = string.IsNullOrEmpty(r.PreviousLicenseId) ? "-" : r.PreviousLicenseId;
            _dgvHistory.Rows.Add(
                r.LicenseId,
                type,
                r.CustomerName,
                r.IssuedAt.ToString("dd-MMM-yyyy"),
                r.ExpiresAt.ToString("dd-MMM-yyyy"),
                prevId,
                r.SerialKey);
        }
    }

    private void OnCopySelectedHistoryKeyClick(object? sender, EventArgs e)
    {
        if (_dgvHistory.SelectedRows.Count > 0)
        {
            var key = _dgvHistory.SelectedRows[0].Cells["SerialKey"].Value?.ToString();
            if (!string.IsNullOrWhiteSpace(key))
            {
                Clipboard.SetText(key);
                MessageBox.Show("Serial key copied to clipboard.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
    #endregion

    #region 5. Tab 4: Key Management
    private Panel BuildKeyManagementTab()
    {
        var scrollPanel = new Panel { AutoScroll = true, BackColor = BgDark, Dock = DockStyle.Fill };
        var card = new Panel
        {
            Location = new Point(0, 0),
            Width = 1140,
            Height = 680,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = PanelDark,
            Padding = new Padding(28, 24, 28, 28)
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(BorderSlate, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        scrollPanel.Controls.Add(card);

        var lblTitle = CreateSectionHeader("🔑 Cryptographic Signing Key Management", 28, 16);
        card.Controls.Add(lblTitle);

        var lblDesc = CreateLabel("DhirDhar uses asymmetric ECDSA NIST P-256 with SHA-256 for offline digital signature creation and verification.", 28, 44);
        lblDesc.ForeColor = TextMuted;
        card.Controls.Add(lblDesc);

        _lblKeyStatus = new Label
        {
            Text = "✓ Official ECDSA P-256 Keypair Active & Embedded",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = GreenAccent,
            BackColor = GreenDark,
            Location = new Point(28, 76),
            Size = new Size(1084, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };
        card.Controls.Add(_lblKeyStatus);

        var lblPubHeader = CreateLabel("Public Verification Key PEM (Embedded in DhirDhar.Infrastructure):", 28, 128);
        lblPubHeader.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        card.Controls.Add(lblPubHeader);

        _txtPublicKeyPem = new TextBox
        {
            Location = new Point(28, 152),
            Size = new Size(1084, 130),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10f, FontStyle.Regular),
            BackColor = InputDark,
            ForeColor = TextHighlight,
            BorderStyle = BorderStyle.FixedSingle,
            Text = LicenseSigner.DefaultPublicKeyPem
        };
        card.Controls.Add(_txtPublicKeyPem);

        _btnCopyPublicKey = new Button
        {
            Text = "📋 Copy Public Key PEM",
            Location = new Point(28, 294),
            Size = new Size(220, 38),
            BackColor = BlueButton,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnCopyPublicKey.FlatAppearance.BorderSize = 0;
        _btnCopyPublicKey.Click += (s, e) =>
        {
            Clipboard.SetText(_txtPublicKeyPem.Text);
            MessageBox.Show("Public verification key copied to clipboard.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        card.Controls.Add(_btnCopyPublicKey);

        var pnlWarning = new Panel
        {
            Location = new Point(28, 352),
            Size = new Size(1084, 96),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = AmberDark
        };
        pnlWarning.Paint += (s, e) =>
        {
            using var pen = new Pen(AmberAccent, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlWarning.Width - 1, pnlWarning.Height - 1);
        };

        var lblWarnText = new Label
        {
            Text = "🔒 Security Notice:\n" +
                   "The ECDSA P-256 private key is strictly confidential and compiled directly into this administrative tool.\n" +
                   "Never distribute this License Generator executable to end-users or clients.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = AmberAccent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 16, 0)
        };
        pnlWarning.Controls.Add(lblWarnText);
        card.Controls.Add(pnlWarning);

        return scrollPanel;
    }
    #endregion

    #region UI Helper Methods
    private static Label CreateSectionHeader(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(x, y),
            AutoSize = true
        };
    }

    private static Label CreateLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(x, y),
            AutoSize = true
        };
    }

    private static TextBox CreateTextBox(int x, int y, int width, string placeholder)
    {
        return new TextBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 30),
            Font = new Font("Segoe UI", 10f),
            BackColor = InputDark,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = placeholder
        };
    }

    private void LoadApplicationIcon()
    {
        try
        {
            var asm = typeof(MainForm).Assembly;
            using var stream = asm.GetManifestResourceStream("DhirDhar.LicenseGenerator.Assets.AppIcon.ico");
            if (stream != null)
            {
                Icon = new Icon(stream);
                return;
            }
        }
        catch
        {
            // Fallback
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
                return;
            }
        }
        catch
        {
            // Fallback
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                var extracted = Icon.ExtractAssociatedIcon(processPath);
                if (extracted != null)
                {
                    Icon = extracted;
                }
            }
        }
        catch
        {
            // Fallback
        }
    }
    #endregion
}
