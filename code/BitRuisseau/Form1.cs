using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
//
namespace BitRuisseau
{
    public partial class MainForm : Form
    {
        private readonly LocalMediaLibrary _localLibrary = new LocalMediaLibrary();
        private readonly IProtocol _protocol;

        private BindingList<Song> _localSongsBinding = new BindingList<Song>();
        private BindingList<Song> _remoteSongsBinding = new BindingList<Song>();

        // Liste complète (avant filtre/tri)
        private List<Song> _allLocalSongs = new List<Song>();

        // État du tri
        private string _currentSortColumn = nameof(Song.Title);
        private bool _currentSortAscending = true;

        public MainForm()
        {
            InitializeComponent();

            InitializeLocalGrid();
            InitializeRemoteGrid();
            HookEvents();

            //_protocol = new Protocole();
            //_protocol.SayOnline();
            _protocol = new Protocole();
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
                Width = 150,
                SortMode = DataGridViewColumnSortMode.Programmatic
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Artist),
                HeaderText = "Artiste",
                Width = 120,
                SortMode = DataGridViewColumnSortMode.Programmatic
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Year),
                HeaderText = "Année",
                Width = 60,
                SortMode = DataGridViewColumnSortMode.Programmatic
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Duration),
                HeaderText = "Durée",
                Width = 80,
                SortMode = DataGridViewColumnSortMode.Programmatic
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Size),
                HeaderText = "Taille (octets)",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.Programmatic
            });
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.FeaturingText),
                HeaderText = "Featuring",
                Width = 150,
                SortMode = DataGridViewColumnSortMode.Programmatic
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
            // Médiathèque locale
            btnSelectFolder.Click += BtnSelectFolder_Click;
            dgvLocalSongs.DoubleClick += DgvLocalSongs_DoubleClick;
            dgvLocalSongs.ColumnHeaderMouseClick += DgvLocalSongs_ColumnHeaderMouseClick;
            txtFilter.TextChanged += TxtFilter_TextChanged;

            // Médiathèques connectées
            btnRefreshMediatheques.Click += BtnRefreshMediatheques_Click;
            lstMediatheques.SelectedIndexChanged += LstMediatheques_SelectedIndexChanged;
            btnImportSong.Click += BtnImportSong_Click;

            // Chargement au démarrage
            this.Load += MainForm_Load;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Restaure le dernier dossier utilisé si possible
            _localLibrary.RestoreLastFolder();

            if (!string.IsNullOrWhiteSpace(_localLibrary.RootFolder))
            {
                lblFolder.Text = _localLibrary.RootFolder;
                _allLocalSongs = _localLibrary.Songs.ToList();
                ApplyLocalFilterAndSort();
            }
            else
            {
                lblFolder.Text = "Aucun dossier sélectionné";
                _allLocalSongs = new List<Song>();
                ApplyLocalFilterAndSort();
            }
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

                    _allLocalSongs = _localLibrary.Songs.ToList();
                    ApplyLocalFilterAndSort();
                }
            }
        }

        private void TxtFilter_TextChanged(object sender, EventArgs e)
        {
            ApplyLocalFilterAndSort();
        }

        private void DgvLocalSongs_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var column = dgvLocalSongs.Columns[e.ColumnIndex];
            var propertyName = column.DataPropertyName;

            if (string.IsNullOrEmpty(propertyName))
                return;

            if (_currentSortColumn == propertyName)
                _currentSortAscending = !_currentSortAscending;
            else
            {
                _currentSortColumn = propertyName;
                _currentSortAscending = true;
            }

            ApplyLocalFilterAndSort();

            // Indicateur visuel de tri
            foreach (DataGridViewColumn col in dgvLocalSongs.Columns)
            {
                col.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
            column.HeaderCell.SortGlyphDirection =
                _currentSortAscending ? SortOrder.Ascending : SortOrder.Descending;
        }

        private void ApplyLocalFilterAndSort()
        {
            IEnumerable<Song> songs = _allLocalSongs;

            var query = txtFilter.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(query))
            {
                songs = songs.Where(s =>
                    (!string.IsNullOrEmpty(s.Title) &&
                     s.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(s.Artist) &&
                     s.Artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (!string.IsNullOrEmpty(_currentSortColumn))
            {
                songs = _currentSortAscending
                    ? songs.OrderBy(s => GetPropertyValue(s, _currentSortColumn))
                    : songs.OrderByDescending(s => GetPropertyValue(s, _currentSortColumn));
            }

            _localSongsBinding = new BindingList<Song>(songs.ToList());
            dgvLocalSongs.DataSource = _localSongsBinding;
        }

        private object GetPropertyValue(Song song, string propertyName)
        {
            var prop = typeof(Song).GetProperty(propertyName);
            return prop?.GetValue(song, null);
        }

        private void DgvLocalSongs_DoubleClick(object sender, EventArgs e)
        {
            if (dgvLocalSongs.CurrentRow == null)
                return;

            var song = dgvLocalSongs.CurrentRow.DataBoundItem as Song;
            if (song == null)
                return;

            // Détail média
            var detail = $"Titre : {song.Title}\n" +
                         $"Artiste : {song.Artist}\n" +
                         $"Année : {song.Year}\n" +
                         $"Durée : {song.Duration}\n" +
                         $"Taille : {song.Size} octets\n" +
                         $"Featuring : {song.FeaturingText}\n" +
                         $"Fichier : {song.FilePath}";

            MessageBox.Show(detail, "Détail du média",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Lecture locale simple pour les WAV (démo)
            try
            {
                if (string.IsNullOrWhiteSpace(song.FilePath) || !File.Exists(song.FilePath))
                    return;

                var ext = Path.GetExtension(song.FilePath).ToLowerInvariant();
                if (ext != ".wav")
                {
                    // Pour la démo on ne lit que les WAV
                    return;
                }

                using (var player = new System.Media.SoundPlayer(song.FilePath))
                {
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la lecture du média : " + ex.Message);
            }
        }

        #endregion

        #region Médiathèques connectées

        private void BtnRefreshMediatheques_Click(object sender, EventArgs e)
        {
            // DISCOVER : on demande au protocole la liste des médiathèques opérationnelles
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
            _allLocalSongs = _localLibrary.Songs.ToList();
            ApplyLocalFilterAndSort();
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

        // Ajout obligatoire pour implémenter ISong
        public string Extension { get; private set; }

        // Non imposé par l’interface
        public string FilePath { get; set; }

        public string FeaturingText =>
            (Featuring == null || Featuring.Length == 0)
            ? string.Empty
            : string.Join(", ", Featuring);

        public Song(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;

            var fi = new FileInfo(filePath);
            Title = Path.GetFileNameWithoutExtension(filePath);
            Artist = "Inconnu";
            Year = DateTime.Now.Year;
            Duration = TimeSpan.Zero;
            Size = (int)fi.Length;
            Featuring = Array.Empty<string>();
            Hash = ComputeHash(filePath);

            // Nouveau : extension du fichier
            Extension = Path.GetExtension(filePath);
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

        // Persistance du choix de dossier dans %AppData%\BitRuisseau
        private string ConfigDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BitRuisseau");

        private string ConfigFilePath =>
            Path.Combine(ConfigDirectory, "localmedialibrary.txt");

        public LocalMediaLibrary()
        {
            Songs = new List<Song>();
        }

        public void SetFolder(string folder)
        {
            RootFolder = folder;
            SaveRootFolder();
            LoadSongs();
        }

        public void RestoreLastFolder()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return;

                var folder = File.ReadAllText(ConfigFilePath).Trim();
                if (string.IsNullOrWhiteSpace(folder))
                    return;

                if (!Directory.Exists(folder))
                    return;

                RootFolder = folder;
                LoadSongs();
            }
            catch
            {
                // En cas de problème de lecture, on ignore et on démarre sans dossier
            }
        }

        private void SaveRootFolder()
        {
            try
            {
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                File.WriteAllText(ConfigFilePath, RootFolder ?? string.Empty);
            }
            catch
            {
                // On ignore les erreurs de persistance pour ne pas bloquer l’application
            }
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
    /// Implémentation "fake" de IProtocol avec discover/announce en mémoire.
    /// </summary>
    internal class FakeProtocol : IProtocol
    {
        private readonly LocalMediaLibrary _localLibrary;
        private readonly string _selfName;

        private static readonly HashSet<string> _onlineMediatheques =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FakeProtocol(LocalMediaLibrary localLibrary)
        {
            _localLibrary = localLibrary;
            _selfName = Environment.MachineName;
        }

        public string[] GetOnlineMediatheque()
        {
            lock (_onlineMediatheques)
            {
                return _onlineMediatheques.ToArray();
            }
        }

        public void SayOnline()
        {
            lock (_onlineMediatheques)
            {
                _onlineMediatheques.Add(_selfName);
            }
        }

        public List<ISong> AskCatalog(string name)
        {
            if (string.Equals(name, _selfName, StringComparison.OrdinalIgnoreCase))
                return _localLibrary.Songs.Cast<ISong>().ToList();

            return new List<ISong>();
        }

        public void SendCatalog(string name)
        {
            // Fake : rien à envoyer réellement
        }

        public void AskMedia(ISong song, string name, int startByte, int endByte)
        {
            // Fake : pas d’implémentation
        }

        public void SendMedia(ISong song, string name, int startByte, int endByte)
        {
            // Fake : pas d’implémentation
        }
    }


    #endregion
}
