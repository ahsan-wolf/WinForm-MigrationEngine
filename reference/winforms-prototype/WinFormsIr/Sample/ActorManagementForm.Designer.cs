namespace LegacyWinFormsApp
{
    partial class ActorManagementForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.btnSearch = new System.Windows.Forms.Button();
            this.btnClear = new System.Windows.Forms.Button();
            this.gvActors = new System.Windows.Forms.DataGridView();
            this.txtFirstName = new System.Windows.Forms.TextBox();
            this.txtLastName = new System.Windows.Forms.TextBox();
            this.txtDOB = new System.Windows.Forms.DateTimePicker();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.actorSearchControl1 = new LegacyWinFormsApp.ActorSearchControl();
            this.SuspendLayout();

            this.txtSearch.Location = new System.Drawing.Point(12, 12);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(200, 20);

            this.btnSearch.Location = new System.Drawing.Point(220, 10);
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.Text = "Search";
            this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);

            this.btnClear.Location = new System.Drawing.Point(300, 10);
            this.btnClear.Name = "btnClear";
            this.btnClear.Text = "Clear";
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);

            this.gvActors.Location = new System.Drawing.Point(12, 45);
            this.gvActors.Name = "gvActors";
            this.gvActors.Size = new System.Drawing.Size(500, 200);
            this.gvActors.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.gvActors_CellClick);

            this.txtFirstName.Location = new System.Drawing.Point(12, 260);
            this.txtFirstName.Name = "txtFirstName";

            this.txtLastName.Location = new System.Drawing.Point(12, 290);
            this.txtLastName.Name = "txtLastName";

            this.txtDOB.Location = new System.Drawing.Point(12, 320);
            this.txtDOB.Name = "txtDOB";

            this.btnSave.Location = new System.Drawing.Point(12, 350);
            this.btnSave.Name = "btnSave";
            this.btnSave.Text = "Save";
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);

            this.btnCancel.Location = new System.Drawing.Point(100, 350);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Text = "Cancel";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

            this.actorSearchControl1.Location = new System.Drawing.Point(12, 400);
            this.actorSearchControl1.Name = "actorSearchControl1";

            this.Controls.Add(this.txtSearch);
            this.Controls.Add(this.btnSearch);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.gvActors);
            this.Controls.Add(this.txtFirstName);
            this.Controls.Add(this.txtLastName);
            this.Controls.Add(this.txtDOB);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.actorSearchControl1);
            this.Name = "ActorManagementForm";
            this.Text = "Actor Management";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Button btnSearch;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.DataGridView gvActors;
        private System.Windows.Forms.TextBox txtFirstName;
        private System.Windows.Forms.TextBox txtLastName;
        private System.Windows.Forms.DateTimePicker txtDOB;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
        private LegacyWinFormsApp.ActorSearchControl actorSearchControl1;
    }
}
