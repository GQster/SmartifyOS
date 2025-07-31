using System;
using System.IO;
using SmartifyOS.LinuxFilePlayer;
using SmartifyOS.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic; 

namespace SmartifyOS.UI.MediaPlayer
{
    public class FilePlayer : BaseUIWindow, IDragHandler, IEndDragHandler, IBeginDragHandler
    {
        [SerializeField] private string filePath;

        [SerializeField] private IconButton previousButton;
        [SerializeField] private IconButton nextButton;
        [SerializeField] private IconButton playButton;
        [SerializeField] private PlayBar progressBar;

        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text artistText;
        [SerializeField] private TMP_Text sourceText;

        [SerializeField] private TMP_Text totalTimeText;
        [SerializeField] private TMP_Text currentTimeText;

        [SerializeField] private IconButton savePlaylistButton;
        [SerializeField] private IconButton loadPlaylistButton;
        [SerializeField] private IconButton addToPlaylistButton;
        [SerializeField] private IconButton removeFromPlaylistButton;
        [SerializeField] private TMP_InputField playlistPathInput;
        [SerializeField] private Transform playlistListParent;
        [SerializeField] private GameObject playlistItemPrefab;
        [SerializeField] private IconButton scrollUpButton;
        [SerializeField] private IconButton scrollDownButton;

        //[SerializeField] private UnityEngine.UI.Image play
        [SerializeField] private Sprite playingSprite;
        [SerializeField] private Sprite pausedSprite;

        [SerializeField] private FilePickerUIWindow filePicker;
        [SerializeField] private IconButton pickFileButton;

        [SerializeField] private BluetoothPlayer bluetoothPlayer;

        [SerializeField] private float timeUntilClosing = 20f;

        private bool playing = false;
        private float timer = 0;

        private Vector2 offset;
        private Vector2 startPosition;
        
        private Playlist playlist = new Playlist();
        private int playlistIndex = 0;
        private const int MaxVisibleItems = 10;
        private int playlistScrollOffset = 0;

        private void Start()
        {
            Init();

            playButton.interactable = false;

            PlayerManager.OnMetadataChanged += PlayerManager_OnMetadataChanged;
            PlayerManager.OnDurationChanged += PlayerManager_OnDurationChanged;
            PlayerManager.OnEndOfFile += PlayerManager_OnEndOfFile;

            bluetoothPlayer.OnOpened += () =>
            {
                Hide();
            };
        }

        private void Awake()
        {
            playButton.onClick += () =>
            {
                if (!PlayerManager.hasInstance)
                {
                    InstantiatePlayer();
                }

                else
                {
                    if (playing)
                    {
                        playing = false;
                        PlayerManager.Instance.Pause();
                        playButton.SetIcon(pausedSprite);
                    }
                    else
                    {
                        playing = true;
                        PlayerManager.Instance.Play();
                        playButton.SetIcon(playingSprite);
                    }
                }
            };

            pickFileButton.onClick += () =>
            {
                filePicker.Show();
            };

            progressBar.OnValueChanged += (value) =>
            {
                float timeStamp = value * PlayerManager.Instance.GetDuration();

                timer = timeStamp;
                PlayerManager.Instance.SkipTo(timeStamp);
            };

            previousButton.onClick += () =>
            {
                if (playlist != null && playlist.audioFilePaths.Count > 0)
                {
                    playlistIndex = Mathf.Max(playlistIndex - 1, 0);
                    SelectAndPlay(playlist.audioFilePaths[playlistIndex]);
                    RefreshPlaylistUI();
                }
            };

            nextButton.onClick += () =>
            {
                if (playlist != null && playlist.audioFilePaths.Count > 0)
                {
                    playlistIndex = Mathf.Min(playlistIndex + 1, playlist.audioFilePaths.Count - 1);
                    SelectAndPlay(playlist.audioFilePaths[playlistIndex]);
                    RefreshPlaylistUI();
                }
            };

            savePlaylistButton.onClick += () =>
            {
                string path = playlistPathInput.text;
                try
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        playlist.Save(path);
                    }
                    else
                    {
                        Debug.LogWarning("Playlist path is empty.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to save playlist: " + ex.Message);
                }
            };

            loadPlaylistButton.onClick += () =>
            {
                string path = playlistPathInput.text;
                try
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        playlist.Load(path);
                        playlistIndex = 0;
                        RefreshPlaylistUI();
                    }
                    else
                    {
                        Debug.LogWarning("Playlist path is empty.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to load playlist: " + ex.Message);
                }
            };

            addToPlaylistButton.onClick += () =>
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    if (File.Exists(filePath))
                    {
                        playlist.Add(filePath);
                        RefreshPlaylistUI();
                    }
                    else
                    {
                        Debug.LogWarning("File does not exist: " + filePath);
                    }
                }
            };

            removeFromPlaylistButton.onClick += () =>
            {
                if (playlist.audioFilePaths.Count > 0 && playlistIndex >= 0 && playlistIndex < playlist.audioFilePaths.Count)
                {
                    playlist.Remove(playlist.audioFilePaths[playlistIndex]);
                    playlistIndex = Mathf.Clamp(playlistIndex, 0, playlist.audioFilePaths.Count - 1);
                    RefreshPlaylistUI();
                }
            };
        
            scrollUpButton.onClick += () =>
            {
                if (playlistScrollOffset > 0)
                {
                    playlistScrollOffset--;
                    RefreshPlaylistUI();
                }
            };

            scrollDownButton.onClick += () =>
            {
                if (playlistScrollOffset < Mathf.Max(0, playlist.audioFilePaths.Count - MaxVisibleItems))
                {
                    playlistScrollOffset++;
                    RefreshPlaylistUI();
                }
            };

        }

        public void AddToPlaylist(string path)
        {
            if (playlist == null) playlist = new Playlist();
            playlist.Add(path);
        }

        public void SelectAndPlay(string path)
        {
            filePath = path;
            playlistIndex = playlist != null ? playlist.audioFilePaths.IndexOf(path) : 0;
            // timer = 0; // Reset timer when switching tracks      DO WE NEED THIS?
            InstantiatePlayer();

            playButton.interactable = true;
        }

        private void PlayerManager_OnEndOfFile()
        {
            UnityMainThreadDispatcher.GetInstance().Enqueue(EndOfFile);
        }

        private void EndOfFile()
        {
            playing = false;
            playButton.SetIcon(pausedSprite);

            Invoke(nameof(AutoHide), timeUntilClosing);
        }

        private void AutoHide()
        {
            Hide();
        }

        private void PlayerManager_OnDurationChanged(float duration)
        {
            totalTimeText.text = FormatTime(duration);
        }

        private void InstantiatePlayer()
        {
            if (filePath == "") return;
            if (!File.Exists(filePath))
            {
                Debug.LogError("File not found: " + filePath);
                return;
            }

            Show(ShowAction.OpenInBackground);

            var file = new FileInfo(filePath);
            sourceText.text = file.Name;

            progressBar.SetValue(0);
            timer = 0;
            playing = true;
            PlayerManager.Instance.StartPlayerInstance(filePath);
            playButton.SetIcon(playingSprite);
            CancelInvoke(nameof(AutoHide));
        }

        private void Update()
        {
            if (PlayerManager.hasInstance && playing)
            {
                timer += Time.deltaTime;
                currentTimeText.text = FormatTime(timer);
                progressBar.SetValue(timer / PlayerManager.Instance.GetDuration());
            }
        }

        private void PlayerManager_OnMetadataChanged(PlayerManager.SongMetadata metadata)
        {
            titleText.text = metadata.title;
            artistText.text = metadata.artist;

            Debug.Log("Metadata other: " + metadata.year + " - " + metadata.album);
        }

        public static string FormatTime(float timeInSeconds)
        {
            int hours = Mathf.FloorToInt(timeInSeconds / 3600);
            int minutes = Mathf.FloorToInt((timeInSeconds % 3600) / 60);
            int seconds = Mathf.FloorToInt(timeInSeconds % 60);

            if (hours > 0)
            {
                return $"{hours}:{minutes:D2}:{seconds:D2}";
            }
            else if (minutes > 0)
            {
                return $"{minutes}:{seconds:D2}";
            }
            else
            {
                return $"{seconds:D2}";
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            offset = eventData.position - (Vector2)transform.position;
            startPosition = transform.position;
            LeanTween.scale(gameObject, Vector3.one * 1.1f, 0.2f).setEaseInOutSine();
        }

        public void OnDrag(PointerEventData eventData)
        {
            transform.position = eventData.position - offset;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Vector2.Distance(transform.position, startPosition) > 100)
            {
                Hide();
                transform.position = startPosition;
            }
            else
            {
                LeanTween.scale(gameObject, Vector3.one, 0.2f).setEaseInOutSine();
                transform.position = startPosition;
            }
        }


        private void RefreshPlaylistUI()
        {
            // Edge case: disable skip buttons if playlist is empty
            bool hasItems = playlist.audioFilePaths.Count > 0;
            previousButton.interactable = hasItems;
            nextButton.interactable = hasItems;

            scrollUpButton.interactable = playlistScrollOffset > 0;
            scrollDownButton.interactable = playlistScrollOffset < Mathf.Max(0, playlist.audioFilePaths.Count - MaxVisibleItems);

            foreach (Transform child in playlistListParent)
            {
                Destroy(child.gameObject);
            }

            // Only show a limited number of items around the current index
            int startIdx = playlistScrollOffset;
            int endIdx = Mathf.Min(playlist.audioFilePaths.Count, startIdx + MaxVisibleItems);

            for (int i = startIdx; i < endIdx; i++)
            {
                var item = Instantiate(playlistItemPrefab, playlistListParent);
                var text = item.GetComponentInChildren<TMP_Text>();
                text.text = Path.GetFileName(playlist.audioFilePaths[i]);
                int idx = i;

                var iconButton = item.GetComponent<IconButton>();
                if (iconButton != null)
                {
                    iconButton.onClick += () =>
                            {
                                playlistIndex = idx;
                                SelectAndPlay(playlist.audioFilePaths[idx]);
                                RefreshPlaylistUI();
                    };
                }

                // Visual feedback: highlight currently playing item
                var bg = item.GetComponent<UnityEngine.UI.Image>();
                if (bg != null)
                {
                    bg.color = (idx == playlistIndex) ? new Color(0.2f, 0.6f, 1f, 0.3f) : Color.white;
                }
            }
        }
    }

    [System.Serializable]
    public class Playlist
    {
        public List<string> audioFilePaths = new List<string>();

        public void Add(string path)
        {
            if (File.Exists(path) && !audioFilePaths.Contains(path))
                audioFilePaths.Add(path);
        }

        public void Remove(string path)
        {
            audioFilePaths.Remove(path);
        }

        public void Save(string savePath)
        {
            File.WriteAllLines(savePath, audioFilePaths);
        }

        public void Load(string loadPath)
        {
            if (File.Exists(loadPath))
                audioFilePaths = new List<string>(File.ReadAllLines(loadPath));
        }
    }

}
