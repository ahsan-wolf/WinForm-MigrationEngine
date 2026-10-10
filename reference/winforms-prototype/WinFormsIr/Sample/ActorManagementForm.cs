using System;
using System.Data;
using System.Data.SqlClient;
using System.Windows.Forms;

namespace LegacyWinFormsApp
{
    public partial class ActorManagementForm : Form
    {
        private string formMode = "Add";
        private int? selectedActorId;

        public ActorManagementForm()
        {
            InitializeComponent();
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            var term = txtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(term)) LoadActors(term);
            else LoadActors();
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtSearch.Text = string.Empty;
            LoadActors();
        }

        private void gvActors_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var actorId = Convert.ToInt32(gvActors.Rows[e.RowIndex].Cells["ActorId"].Value);
            LoadActorForEdit(actorId);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                if (formMode == "Edit") UpdateActor();
                else InsertActor();

                ClearForm();
                LoadActors();
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            ClearForm();
            formMode = "Add";
        }

        private void LoadActors(string searchTerm = null)
        {
            var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            using (var conn = new SqlConnection(connectionString))
            {
                var query = "SELECT ActorId, FirstName, LastName, DateOfBirth FROM Actors";
                if (!string.IsNullOrEmpty(searchTerm))
                    query += " WHERE FirstName LIKE @Search OR LastName LIKE @Search";
                query += " ORDER BY LastName, FirstName";

                using (var cmd = new SqlCommand(query, conn))
                {
                    if (!string.IsNullOrEmpty(searchTerm))
                        cmd.Parameters.AddWithValue("@Search", "%" + searchTerm + "%");

                    conn.Open();
                    var da = new SqlDataAdapter(cmd);
                    var dt = new DataTable();
                    da.Fill(dt);
                    gvActors.DataSource = dt;
                }
            }
        }

        private void LoadActorForEdit(int actorId)
        {
            var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            using (var conn = new SqlConnection(connectionString))
            {
                var query = "SELECT * FROM Actors WHERE ActorId = @ActorId";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ActorId", actorId);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            selectedActorId = actorId;
                            txtFirstName.Text = reader["FirstName"].ToString();
                            txtLastName.Text = reader["LastName"].ToString();
                            txtDOB.Value = Convert.ToDateTime(reader["DateOfBirth"]);
                            formMode = "Edit";
                        }
                    }
                }
            }
        }

        private void InsertActor()
        {
            var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            using (var conn = new SqlConnection(connectionString))
            {
                var query = "INSERT INTO Actors (FirstName, LastName, DateOfBirth) VALUES (@FirstName, @LastName, @DOB)";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FirstName", txtFirstName.Text);
                    cmd.Parameters.AddWithValue("@LastName", txtLastName.Text);
                    cmd.Parameters.AddWithValue("@DOB", txtDOB.Value);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void UpdateActor()
        {
            var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            using (var conn = new SqlConnection(connectionString))
            {
                var query = "UPDATE Actors SET FirstName = @FirstName, LastName = @LastName, DateOfBirth = @DOB WHERE ActorId = @ActorId";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ActorId", selectedActorId);
                    cmd.Parameters.AddWithValue("@FirstName", txtFirstName.Text);
                    cmd.Parameters.AddWithValue("@LastName", txtLastName.Text);
                    cmd.Parameters.AddWithValue("@DOB", txtDOB.Value);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(txtFirstName.Text) || string.IsNullOrWhiteSpace(txtLastName.Text))
                return false;
            return true;
        }

        private void ClearForm()
        {
            selectedActorId = null;
            txtFirstName.Text = string.Empty;
            txtLastName.Text = string.Empty;
        }
    }
}
