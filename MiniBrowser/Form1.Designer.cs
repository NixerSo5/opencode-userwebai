namespace MiniBrowser;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    private ToolStrip toolStrip;
    private ToolStripButton btnBack;
    private ToolStripButton btnForward;
    private ToolStripButton btnRefresh;
    private ToolStripButton btnRecord;
    private ToolStripButton btnClear;
    private ToolStripTextBox addressBox;
    private ToolStripComboBox siteCombo;
    private ToolStripButton btnJump;
    private ToolStripButton btnGo;
    private ToolStripLabel lblCount;
    private ToolStripLabel lblService;
    private Microsoft.Web.WebView2.WinForms.WebView2 webView;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        toolStrip = new ToolStrip();
        btnBack = new ToolStripButton();
        btnForward = new ToolStripButton();
        btnRefresh = new ToolStripButton();
        btnRecord = new ToolStripButton();
        btnClear = new ToolStripButton();
        addressBox = new ToolStripTextBox();
        siteCombo = new ToolStripComboBox();
        btnJump = new ToolStripButton();
        btnGo = new ToolStripButton();
        lblCount = new ToolStripLabel();
        lblService = new ToolStripLabel();
        webView = new Microsoft.Web.WebView2.WinForms.WebView2();
        toolStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)webView).BeginInit();
        SuspendLayout();

        btnBack.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnBack.Name = "btnBack";
        btnBack.Size = new Size(30, 26);
        btnBack.Text = "←";
        btnBack.ToolTipText = "后退 (Alt+←)";

        btnForward.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnForward.Name = "btnForward";
        btnForward.Size = new Size(30, 26);
        btnForward.Text = "→";
        btnForward.ToolTipText = "前进 (Alt+→)";

        btnRefresh.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new Size(52, 26);
        btnRefresh.Text = "刷新";
        btnRefresh.ToolTipText = "刷新 (F5)";

        addressBox.AutoSize = false;
        addressBox.Font = new Font("Segoe UI", 10F);
        addressBox.Margin = new Padding(4, 1, 2, 1);
        addressBox.Name = "addressBox";
        addressBox.Size = new Size(600, 26);

        siteCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        siteCombo.Name = "siteCombo";
        siteCombo.Size = new Size(140, 26);
        siteCombo.ToolTipText = "选择站点";

        btnJump.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnJump.Name = "btnJump";
        btnJump.Size = new Size(46, 26);
        btnJump.Text = "跳转";
        btnJump.ToolTipText = "打开所选站点";

        btnGo.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnGo.Name = "btnGo";
        btnGo.Size = new Size(46, 26);
        btnGo.Text = "转到";

        btnRecord.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnRecord.Enabled = false;
        btnRecord.Name = "btnRecord";
        btnRecord.Size = new Size(72, 26);
        btnRecord.Text = "开始记录";

        btnClear.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnClear.Enabled = false;
        btnClear.Name = "btnClear";
        btnClear.Size = new Size(72, 26);
        btnClear.Text = "清空记录";

        lblCount.Name = "lblCount";
        lblCount.Padding = new Padding(8, 0, 0, 0);

        lblService.Name = "lblService";
        lblService.Padding = new Padding(12, 0, 0, 0);
        lblService.Text = "API: 启动中...";

        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Items.AddRange(new ToolStripItem[]
        {
            btnBack, btnForward, btnRefresh, btnRecord, btnClear, addressBox, btnGo, siteCombo, btnJump, lblCount, lblService
        });
        toolStrip.Padding = new Padding(6, 2, 4, 2);
        toolStrip.Dock = DockStyle.Top;
        toolStrip.Name = "toolStrip";

        webView.Dock = DockStyle.Fill;
        webView.DefaultBackgroundColor = Color.White;
        webView.Name = "webView";

        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 800);
        Text = "MiniBrowser";

        Controls.Add(webView);
        Controls.Add(toolStrip);

        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)webView).EndInit();
        ResumeLayout(false);
    }

    #endregion
}
