namespace LegacyWinFormsApp
{
    partial class ActorSearchControl
    {
        private void InitializeComponent()
        {
            this.txtQuickFind = new System.Windows.Forms.TextBox();
            this.btnGo = new System.Windows.Forms.Button();
            this.SuspendLayout();

            this.txtQuickFind.Location = new System.Drawing.Point(3, 3);
            this.txtQuickFind.Name = "txtQuickFind";

            this.btnGo.Location = new System.Drawing.Point(150, 3);
            this.btnGo.Name = "btnGo";
            this.btnGo.Text = "Go";
            this.btnGo.Click += new System.EventHandler(this.btnGo_Click);

            this.Controls.Add(this.txtQuickFind);
            this.Controls.Add(this.btnGo);
            this.Name = "ActorSearchControl";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TextBox txtQuickFind;
        private System.Windows.Forms.Button btnGo;
    }
}
