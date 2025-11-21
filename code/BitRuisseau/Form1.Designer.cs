using System.Drawing;
using System.Windows.Forms;

namespace BitRuisseau
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnSelectFolder = new Button();
            lblFolder = new Label();
            dgvLocalSongs = new DataGridView();
            btnRefreshMediatheques = new Button();
            lstMediatheques = new ListBox();
            dgvRemoteSongs = new DataGridView();
            btnImportSong = new Button();
            lblFilter = new Label();
            txtFilter = new TextBox();
            ((System.ComponentModel.ISupportInitialize)dgvLocalSongs).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dgvRemoteSongs).BeginInit();
            SuspendLayout();
            // 
            // btnSelectFolder
            // 
            btnSelectFolder.Location = new Point(23, 12);
            btnSelectFolder.Name = "btnSelectFolder";
            btnSelectFolder.Size = new Size(120, 23);
            btnSelectFolder.TabIndex = 0;
            btnSelectFolder.Text = "Choisir dossier";
            btnSelectFolder.UseVisualStyleBackColor = true;
            // 
            // lblFolder
            // 
            lblFolder.AutoSize = true;
            lblFolder.Location = new Point(160, 16);
            lblFolder.Name = "lblFolder";
            lblFolder.Size = new Size(143, 15);
            lblFolder.TabIndex = 1;
            lblFolder.Text = "Aucun dossier sélectionné";
            // 
            // lblFilter
            // 
            lblFilter.AutoSize = true;
            lblFilter.Location = new Point(23, 50);
            lblFilter.Name = "lblFilter";
            lblFilter.Size = new Size(37, 15);
            lblFilter.TabIndex = 7;
            lblFilter.Text = "Filtre :";
            // 
            // txtFilter
            // 
            txtFilter.Location = new Point(66, 47);
            txtFilter.Name = "txtFilter";
            txtFilter.Size = new Size(237, 23);
            txtFilter.TabIndex = 1;
            // 
            // dgvLocalSongs
            // 
            dgvLocalSongs.AllowUserToAddRows = false;
            dgvLocalSongs.AllowUserToDeleteRows = false;
            dgvLocalSongs.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvLocalSongs.Location = new Point(23, 85);
            dgvLocalSongs.Name = "dgvLocalSongs";
            dgvLocalSongs.ReadOnly = true;
            dgvLocalSongs.Size = new Size(400, 315);
            dgvLocalSongs.TabIndex = 2;
            // 
            // btnRefreshMediatheques
            // 
            btnRefreshMediatheques.Location = new Point(450, 12);
            btnRefreshMediatheques.Name = "btnRefreshMediatheques";
            btnRefreshMediatheques.Size = new Size(130, 23);
            btnRefreshMediatheques.TabIndex = 3;
            btnRefreshMediatheques.Text = "Rafraîchir médiathèques";
            btnRefreshMediatheques.UseVisualStyleBackColor = true;
            // 
            // lstMediatheques
            // 
            lstMediatheques.FormattingEnabled = true;
            lstMediatheques.ItemHeight = 15;
            lstMediatheques.Location = new Point(450, 47);
            lstMediatheques.Name = "lstMediatheques";
            lstMediatheques.Size = new Size(200, 94);
            lstMediatheques.TabIndex = 4;
            // 
            // dgvRemoteSongs
            // 
            dgvRemoteSongs.AllowUserToAddRows = false;
            dgvRemoteSongs.AllowUserToDeleteRows = false;
            dgvRemoteSongs.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvRemoteSongs.Location = new Point(450, 185);
            dgvRemoteSongs.Name = "dgvRemoteSongs";
            dgvRemoteSongs.ReadOnly = true;
            dgvRemoteSongs.Size = new Size(320, 215);
            dgvRemoteSongs.TabIndex = 6;
            // 
            // btnImportSong
            // 
            btnImportSong.Location = new Point(668, 47);
            btnImportSong.Name = "btnImportSong";
            btnImportSong.Size = new Size(102, 23);
            btnImportSong.TabIndex = 5;
            btnImportSong.Text = "Importer";
            btnImportSong.UseVisualStyleBackColor = true;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(txtFilter);
            Controls.Add(lblFilter);
            Controls.Add(btnImportSong);
            Controls.Add(dgvRemoteSongs);
            Controls.Add(lstMediatheques);
            Controls.Add(btnRefreshMediatheques);
            Controls.Add(dgvLocalSongs);
            Controls.Add(lblFolder);
            Controls.Add(btnSelectFolder);
            Name = "MainForm";
            Text = "BitRuisseau";
            ((System.ComponentModel.ISupportInitialize)dgvLocalSongs).EndInit();
            ((System.ComponentModel.ISupportInitialize)dgvRemoteSongs).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnSelectFolder;
        private Label lblFolder;
        private DataGridView dgvLocalSongs;
        private Button btnRefreshMediatheques;
        private ListBox lstMediatheques;
        private DataGridView dgvRemoteSongs;
        private Button btnImportSong;
        private Label lblFilter;
        private TextBox txtFilter;
    }
}
