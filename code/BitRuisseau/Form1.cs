using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BitRuisseau
{
    public partial class MainForm : Form
    {
        private readonly LocalMediaLibrary _localLibrary = new LocalMediaLibrary();
        private readonly IProtocol _protocol;

        private BindingList<Song> _localSongsBinding = new BindingList<Song>();
        private BindingList<Song> _remoteSongsBinding = new BindingList<Song>();

        public MainForm()
        {
            InitializeComponent();

            InitializeLocalGrid();
            InitializeRemoteGrid();
            HookEvents();

            // Implémentation "fake" du protocole, uniquement pour la démo
            _protocol = new FakeProtocol(_localLibrary);
        }

        #region Initialisation UI

        private void InitializeLocalGrid()
        {
            dgvLocalSongs.AutoGenerateColumns = false;
            dgvLocalSongs.ReadOnly = true;
            dgvLocalSongs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvLocalSongs.MultiSelect = false;

            dgvLocalSongs.Columns.Clear();

            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Title),
                HeaderText = "Titre",
                Width = 150
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Artist),
                HeaderText = "Artiste",
                Width = 120
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Year),
                HeaderText = "Année",
                Width = 60
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Duration),
                HeaderText = "Durée",
                Width = 80
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Size),
                HeaderText = "Taille (octets)",
                Width = 100
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.FeaturingText),
                HeaderText = "Featuring",
                Width = 150
            });

            _localSongsBinding = new BindingList<Song>();
            dgvLocalSongs.DataSource = _localSongsBinding;
        }

        private void InitializeRemoteGrid()
        {
            dgvRemoteSongs.AutoGenerateColumns = false;
            dgvRemoteSongs.ReadOnly = true;
            dgvRemoteSongs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvRemoteSongs.MultiSelect = false;

            dgvRemoteSongs.Columns.Clear();

            dgvRemoteSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Title),
                HeaderText = "Titre",
                Width = 150
            });
            dgvRemoteSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Artist),
                HeaderText = "Artiste",
                Width = 120
            });

            _remoteSongsBinding = new BindingList<Song>();
            dgvRemoteSongs.DataSource = _remoteSongsBinding;
        }

        private void HookEvents()
        {
            btnSelectFolder.Click += BtnSelectFolder_Click;
            dgvLocalSongs.DoubleClick += DgvLocalSongs_DoubleClick;

            btnRefreshMediatheques.Click += BtnRefreshMediatheques_Click;
            lstMediatheques.SelectedIndexChanged += LstMediatheques_SelectedIndexChanged;
            btnImportSong.Click += BtnImportSong_Click;
        }

        #endregion

        #region Médiathèque locale (standalone)

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _localLibrary.SetFolder(dlg.SelectedPath);
                    lblFolder.Text = dlg.SelectedPath;

                    _localSongsBinding = new BindingList<Song>(_localLibrary.Songs);
                    dgvLocalSongs.DataSource = _localSongsBinding;
                }
            }
        }

        private void DgvLocalSongs_DoubleClick(object sender, EventArgs e)
        {
            if (dgvLocalSongs.CurrentRow != null)
            {
                var song = dgvLocalSongs.CurrentRow.DataBoundItem as Song;
                if (song != null && !string.IsNullOrWhiteSpace(song.FilePath))
                {
                    wmpPlayer.URL = song.FilePath;
                    wmpPlayer.Ctlcontrols.play();
                }
            }
        }

        #endregion

        #region Médiathèques connectées

        private void BtnRefreshMediatheques_Click(object sender, EventArgs e)
        {
            var online = _protocol.GetOnlineMediatheque() ?? new string[0];
            lstMediatheques.DataSource = online;
        }

        private void LstMediatheques_SelectedIndexChanged(object sender, EventArgs e)
        {
            var name = lstMediatheques.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Récupération du catalogue distant
            var songs = _protocol.AskCatalog(name) ?? new List<ISong>();

            // On garde uniquement les implémentations Song pour l’affichage
            var casted = songs
                .OfType<Song>()
                .ToList();

            _remoteSongsBinding = new BindingList<Song>(casted);
            dgvRemoteSongs.DataSource = _remoteSongsBinding;
        }

        private void BtnImportSong_Click(object sender, EventArgs e)
        {
            if (dgvRemoteSongs.CurrentRow == null)
                return;

            var current = dgvRemoteSongs.CurrentRow.DataBoundItem as Song;
            if (current == null)
                return;

            var remoteSong = current;

            // Dans une vraie implémentation :
            // 1. _protocol.AskMedia(...)
            // 2. Réception Message avec SongData (base64)
            // 3. Conversion en bytes + File.WriteAllBytes(...)
            // 4. Rechargement de la médiathèque locale

            // Ici, comme FakeProtocol renvoie les mêmes fichiers locaux, on simule un import
            // en copiant simplement le fichier dans le dossier local s'il existe.

            if (string.IsNullOrWhiteSpace(_localLibrary.RootFolder))
            {
                MessageBox.Show("Aucun dossier local configuré.");
                return;
            }

            if (string.IsNullOrWhiteSpace(remoteSong.FilePath) || !File.Exists(remoteSong.FilePath))
            {
                MessageBox.Show("Ce morceau n’a pas de fichier associé.");
                return;
            }

            var extension = Path.GetExtension(remoteSong.FilePath);
            var destPath = Path.Combine(
                _localLibrary.RootFolder,
                string.Format("{0}{1}", remoteSong.Title, extension)
            );

            // Si le fichier existe déjà, on ne recopie pas
            if (!File.Exists(destPath))
            {
                File.Copy(remoteSong.FilePath, destPath, false);
            }

            // Recharge la médiathèque locale
            _localLibrary.SetFolder(_localLibrary.RootFolder);
            _localSongsBinding = new BindingList<Song>(_localLibrary.Songs);
            dgvLocalSongs.DataSource = _localSongsBinding;
        }

        #endregion
    }

    #region Classes internes (dans le même fichier, sans toucher aux autres .cs)

    /// <summary>
    /// Implémentation concrète de ISong pour l’UI.
    /// </summary>
    internal class Song : ISong
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public int Year { get; set; }
        public TimeSpan Duration { get; set; }
        public int Size { get; set; }
        public string[] Featuring { get; set; }
        public string Hash { get; private set; }

        // Propriété supplémentaire, non imposée par l’interface
        public string FilePath { get; set; }

        // Pour affichage dans le DataGridView
        public string FeaturingText
        {
            get
            {
                if (Featuring == null || Featuring.Length == 0)
                    return string.Empty;
                return string.Join(", ", Featuring);
            }
        }

        public Song(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException("filePath");

            FilePath = filePath;

            var fi = new FileInfo(filePath);
            Title = Path.GetFileNameWithoutExtension(filePath);
            Artist = "Inconnu";
            Year = DateTime.Now.Year;
            Duration = TimeSpan.Zero; // Tu peux utiliser TagLib# si tu veux une vraie durée
            Size = (int)fi.Length;
            Featuring = new string[0];
            Hash = ComputeHash(filePath);
        }

        private static string ComputeHash(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Médiathèque locale, chargée à partir d’un dossier.
    /// </summary>
    internal class LocalMediaLibrary
    {
        public string RootFolder { get; private set; }
        public List<Song> Songs { get; private set; }

        public LocalMediaLibrary()
        {
            Songs = new List<Song>();
        }

        public void SetFolder(string folder)
        {
            RootFolder = folder;
            LoadSongs();
        }

        private void LoadSongs()
        {
            if (string.IsNullOrWhiteSpace(RootFolder) || !Directory.Exists(RootFolder))
            {
                Songs = new List<Song>();
                return;
            }

            var extensions = new[] { ".mp3", ".wav", ".flac", ".ogg" };

            Songs = Directory
                .EnumerateFiles(RootFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .Select(path => new Song(path)) // LINQ
                .ToList();
        }
    }

    /// <summary>
    /// Implémentation très simple de IProtocol.
    /// Tout reste en local, juste pour que l’UI fonctionne.
    /// </summary>
    internal class FakeProtocol : IProtocol
    {
        private readonly LocalMediaLibrary _localLibrary;

        public FakeProtocol(LocalMediaLibrary localLibrary)
        {
            _localLibrary = localLibrary;
        }

        public string[] GetOnlineMediatheque()
        {
            // Pour la démo, on considère qu’il y a toujours UNE médiathèque: "LocalDemo"
            return new[] { "LocalDemo" };
        }

        public void SayOnline()
        {
            // Rien à faire dans la version fake
        }

        public List<ISong> AskCatalog(string name)
        {
            // Si on demande "LocalDemo", on renvoie notre catalogue local.
            if (name == "LocalDemo")
                return _localLibrary.Songs.Cast<ISong>().ToList();

            return new List<ISong>();
        }

        public void SendCatalog(string name)
        {
            // Non utilisé dans la version fake
        }

        public void AskMedia(string name, int startByte, int endByte)
        {
            // Non utilisé dans la version fake
        }

        public void SendMedia(string name, int startByte, int endByte)
        {
            // Non utilisé dans la version fake
        }
    }

    #endregion
}
