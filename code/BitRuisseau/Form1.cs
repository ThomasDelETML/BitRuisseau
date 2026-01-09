using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BitRuisseau
{
    public partial class MainForm : Form
    {
        // Médiathèque locale : scan d'un dossier, création d'objets Song.
        private readonly LocalMediaLibrary _localLibrary = new LocalMediaLibrary();

        // Protocole réseau : discover, catalogue, et import média.
        private readonly IProtocol _protocol;

        // Binding UI :
        // - local = Song
        // - distant = RemoteSong
        private BindingList<Song> _localSongsBinding = new BindingList<Song>();
        private BindingList<RemoteSong> _remoteSongsBinding = new BindingList<RemoteSong>();

        // Liste complète locale (utilisée pour filtrer/ trier sans recharger du disque à chaque frappe).
        private List<Song> _allLocalSongs = new List<Song>();

        // État du tri : colonne + sens.
        private string _currentSortColumn = nameof(Song.Title);
        private bool _currentSortAscending = true;

        public MainForm()
        {
            InitializeComponent();

            InitializeLocalGrid();
            InitializeRemoteGrid();
            HookEvents();

            // Protocole MQTT réel.
            _protocol = new Protocole(_localLibrary, username: "ict", password: "321");
        }

        #region Initialisation UI

        private void InitializeLocalGrid()
        {
            // Configuration de base de la grille locale.
            dgvLocalSongs.AutoGenerateColumns = false;
            dgvLocalSongs.ReadOnly = true;
            dgvLocalSongs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvLocalSongs.MultiSelect = false;

            dgvLocalSongs.Columns.Clear();

            // Colonnes locales liées aux propriétés de Song.
            dgvLocalSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Song.Title),
                HeaderText = "Titre",
                Width = 150,
                SortMode = DataGridViewColumnSortMode.Programmatic // tri géré manuellement
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
            // Configuration de base de la grille distante.
            dgvRemoteSongs.AutoGenerateColumns = false;
            dgvRemoteSongs.ReadOnly = true;
            dgvRemoteSongs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvRemoteSongs.MultiSelect = false;

            dgvRemoteSongs.Columns.Clear();

            // Colonnes distantes liées aux propriétés de RemoteSong.
            dgvRemoteSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(RemoteSong.Title),
                HeaderText = "Titre",
                Width = 150
            });
            dgvRemoteSongs.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(RemoteSong.Artist),
                HeaderText = "Artiste",
                Width = 120
            });

            _remoteSongsBinding = new BindingList<RemoteSong>();
            dgvRemoteSongs.DataSource = _remoteSongsBinding;
        }

        private void HookEvents()
        {
            // Événements UI : local.
            btnSelectFolder.Click += BtnSelectFolder_Click;
            dgvLocalSongs.DoubleClick += DgvLocalSongs_DoubleClick;
            dgvLocalSongs.ColumnHeaderMouseClick += DgvLocalSongs_ColumnHeaderMouseClick;
            txtFilter.TextChanged += TxtFilter_TextChanged;

            // Événements UI : distant.
            btnRefreshMediatheques.Click += BtnRefreshMediatheques_Click;
            lstMediatheques.SelectedIndexChanged += LstMediatheques_SelectedIndexChanged;
            btnImportSong.Click += BtnImportSong_Click;

            // Chargement initial du formulaire.
            this.Load += MainForm_Load;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Restaure le dernier dossier local sélectionné (si disponible).
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

        #region Médiathèque locale

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            // Sélection d'un dossier local contenant des fichiers audio.
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _localLibrary.SetFolder(dlg.SelectedPath);
                    lblFolder.Text = dlg.SelectedPath;

                    // Mise à jour des données UI (liste complète + filtre/tri).
                    _allLocalSongs = _localLibrary.Songs.ToList();
                    ApplyLocalFilterAndSort();
                }
            }
        }

        private void TxtFilter_TextChanged(object sender, EventArgs e)
        {
            // Filtre dynamique sur la liste locale.
            ApplyLocalFilterAndSort();
        }

        private void DgvLocalSongs_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            // Tri manuel (colonne cliquée).
            var column = dgvLocalSongs.Columns[e.ColumnIndex];
            var propertyName = column.DataPropertyName;

            if (string.IsNullOrEmpty(propertyName))
                return;

            // Même colonne -> inversion du sens.
            if (_currentSortColumn == propertyName)
                _currentSortAscending = !_currentSortAscending;
            else
            {
                // Nouvelle colonne -> tri ascendant.
                _currentSortColumn = propertyName;
                _currentSortAscending = true;
            }

            ApplyLocalFilterAndSort();

            // Affichage du glyph de tri.
            foreach (DataGridViewColumn col in dgvLocalSongs.Columns)
            {
                col.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
            column.HeaderCell.SortGlyphDirection =
                _currentSortAscending ? SortOrder.Ascending : SortOrder.Descending;
        }

        private void ApplyLocalFilterAndSort()
        {
            // 1) point de départ : liste complète locale.
            IEnumerable<Song> songs = _allLocalSongs;

            // 2) filtre : Titre ou Artiste contient la recherche.
            var query = txtFilter.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(query))
            {
                songs = songs.Where(s =>
                    (!string.IsNullOrEmpty(s.Title) &&
                     s.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(s.Artist) &&
                     s.Artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // 3) tri : selon la colonne choisie et le sens courant.
            if (!string.IsNullOrEmpty(_currentSortColumn))
            {
                songs = _currentSortAscending
                    ? songs.OrderBy(s => GetPropertyValue(s, _currentSortColumn))
                    : songs.OrderByDescending(s => GetPropertyValue(s, _currentSortColumn));
            }

            // 4) ré-affectation de la source pour rafraîchir l'affichage.
            _localSongsBinding = new BindingList<Song>(songs.ToList());
            dgvLocalSongs.DataSource = _localSongsBinding;
        }

        private object GetPropertyValue(Song song, string propertyName)
        {
            // Reflection : récupère dynamiquement la valeur de propriété (tri générique).
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

            // Détails d'un morceau local.
            var detail = $"Titre : {song.Title}\n" +
                         $"Artiste : {song.Artist}\n" +
                         $"Année : {song.Year}\n" +
                         $"Durée : {song.Duration}\n" +
                         $"Taille : {song.Size} octets\n" +
                         $"Featuring : {song.FeaturingText}\n" +
                         $"Fichier : {song.FilePath}";

            MessageBox.Show(detail, "Détail du média",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Démonstration : lecture uniquement WAV.
            try
            {
                if (string.IsNullOrWhiteSpace(song.FilePath) || !File.Exists(song.FilePath))
                    return;

                var ext = Path.GetExtension(song.FilePath).ToLowerInvariant();
                if (ext != ".wav")
                {
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
            // Demande au protocole la liste des médiathèques "online" connues.
            var online = _protocol.GetOnlineMediatheque() ?? new string[0];

            // Affiche la liste dans la ListBox.
            lstMediatheques.DataSource = online;
        }

        private void LstMediatheques_SelectedIndexChanged(object sender, EventArgs e)
        {
            var name = lstMediatheques.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Demande du catalogue distant :
            // - envoi askCatalog
            // - attente sendCatalog
            // - conversion en RemoteSong côté protocole
            var songs = _protocol.AskCatalog(name) ?? new List<ISong>();

            // Cast ISong -> RemoteSong (objet distant pour l'affichage).
            var casted = songs
                .OfType<RemoteSong>()
                .ToList();

            _remoteSongsBinding = new BindingList<RemoteSong>(casted);
            dgvRemoteSongs.DataSource = _remoteSongsBinding;
        }

        private async void BtnImportSong_Click(object sender, EventArgs e)
        {
            // Import : déclenche un téléchargement du média distant vers le dossier local.
            if (dgvRemoteSongs.CurrentRow == null)
                return;

            var remote = dgvRemoteSongs.CurrentRow.DataBoundItem as RemoteSong;
            if (remote == null)
                return;

            // La médiathèque distante sélectionnée sert de "remoteHost" (Sender attendu).
            var remoteHost = lstMediatheques.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(remoteHost))
            {
                MessageBox.Show("Aucune médiathèque distante sélectionnée.");
                return;
            }

            // Dossier destination = dossier de la médiathèque locale sélectionné.
            if (string.IsNullOrWhiteSpace(_localLibrary.RootFolder))
            {
                MessageBox.Show("Aucun dossier local configuré (Sélectionner dossier).");
                return;
            }

            if (!Directory.Exists(_localLibrary.RootFolder))
            {
                MessageBox.Show("Le dossier local n'existe pas : " + _localLibrary.RootFolder);
                return;
            }

            // Accès à la méthode ImportRemoteSongAsync (spécifique à Protocole).
            if (!(_protocol is Protocole proto))
            {
                MessageBox.Show("Protocole incompatible.");
                return;
            }

            btnImportSong.Enabled = false;

            try
            {
                // Progression simple : mise à jour du titre de fenêtre.
                // (Il est possible de remplacer par une ProgressBar si nécessaire.)
                var progress = new Progress<int>(pct =>
                {
                    this.Text = $"BitRuisseau - Import {pct}%";
                });

                // Téléchargement :
                // - écrit d'abord un fichier ".part" (fichier temporaire incomplet)
                // - vérifie le SHA256
                // - renomme en fichier final si OK
                var savedPath = await proto.ImportRemoteSongAsync(remote, remoteHost, _localLibrary.RootFolder, progress);

                // Recharge la médiathèque locale (pour inclure le fichier importé).
                _localLibrary.SetFolder(_localLibrary.RootFolder);
                _allLocalSongs = _localLibrary.Songs.ToList();
                ApplyLocalFilterAndSort();

                MessageBox.Show($"Import terminé :\n{savedPath}");

                // Ouvrir l'explorateur sur le fichier importé.
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{savedPath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur import : " + ex.Message);
            }
            finally
            {
                this.Text = "BitRuisseau";
                btnImportSong.Enabled = true;
            }
        }

        #endregion
    }

    #region Classes internes (dans le même fichier, sans toucher aux autres .cs)

    // Song = représentation locale d'un fichier audio.
    // - FilePath : chemin local
    // - Hash : SHA256 du fichier (identifiant fort pour AskMedia/Import)
    internal class Song : ISong
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public int Year { get; set; }
        public TimeSpan Duration { get; set; }
        public int Size { get; set; }
        public string[] Featuring { get; set; }
        public string Hash { get; private set; }
        public string Extension { get; private set; }
        public string FilePath { get; set; }

        // Champs d'affichage : Featuring sous forme texte.
        public string FeaturingText =>
            (Featuring == null || Featuring.Length == 0)
            ? string.Empty
            : string.Join(", ", Featuring);

        public Song(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            FilePath = filePath;

            var fi = new FileInfo(filePath);

            // Valeurs par défaut (si métadonnées non disponibles).
            Title = Path.GetFileNameWithoutExtension(filePath);
            Artist = "Inconnu";
            Year = 0;
            Duration = TimeSpan.Zero;
            Size = (int)fi.Length;
            Featuring = Array.Empty<string>();

            Extension = Path.GetExtension(filePath);

            // Hash SHA256 calculé sur le fichier local.
            Hash = ComputeHash(filePath);
        }

        private static string ComputeHash(string filePath)
        {
            // Calcul SHA256 sur le fichier (lecture stream).
            using (var stream = File.OpenRead(filePath))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    // LocalMediaLibrary : scan d'un dossier et construction de la liste Song.
    // Persiste aussi le dernier dossier choisi dans AppData.
    internal class LocalMediaLibrary
    {
        public string RootFolder { get; private set; }
        public List<Song> Songs { get; private set; }

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
                // Lecture config échoue -> démarrage sans dossier.
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
                // Persistance non critique -> ignorée.
            }
        }

        private void LoadSongs()
        {
            if (string.IsNullOrWhiteSpace(RootFolder) || !Directory.Exists(RootFolder))
            {
                Songs = new List<Song>();
                return;
            }

            // Extensions audio autorisées.
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".wav", ".flac", ".ogg"
            };

            var list = new List<Song>();

            // Scan récursif du dossier.
            foreach (var path in Directory.EnumerateFiles(RootFolder, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (!allowed.Contains(ext))
                        continue;

                    list.Add(new Song(path));
                }
                catch
                {
                    // Un fichier peut poser problème (droits, fichier corrompu, etc.) -> ignoré.
                }
            }

            Songs = list;
        }
    }

    // FakeProtocol : implémentation de test (sans réseau), utile pour valider uniquement l'UI.
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
        }

        public void AskMedia(ISong song, string name, int startByte, int endByte)
        {
        }

        public void SendMedia(ISong song, string name, int startByte, int endByte)
        {
        }
    }

    #endregion
}
