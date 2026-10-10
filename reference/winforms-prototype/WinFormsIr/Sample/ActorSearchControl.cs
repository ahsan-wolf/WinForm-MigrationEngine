using System;
using System.Windows.Forms;

namespace LegacyWinFormsApp
{
    public partial class ActorSearchControl : UserControl
    {
        public event EventHandler<string> QuickFindRequested;

        public ActorSearchControl()
        {
            InitializeComponent();
        }

        private void btnGo_Click(object sender, EventArgs e)
        {
            QuickFindRequested?.Invoke(this, txtQuickFind.Text);
        }
    }
}
