﻿using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Media;
using ReactiveUI;
using TMSpeech.Core;
using TMSpeech.Core.Plugins;

namespace TMSpeech.GUI.ViewModels
{
    class ConfigJsonValueAttribute : Attribute
    {
    }

    public abstract class ConfigViewModelBase : ViewModelBase
    {
        protected abstract string SectionName { get; }

        [Reactive]
        public bool IsModified { get; protected set; }

        [Reactive]
        public bool IsDirty { get; protected set; }

        private void UpdateModifyStatus()
        {
            IsModified = ConfigManagerFactory.Instance.IsModified;
        }

        private void UpdateDirtyStatus()
        {
            var value1 = Serialize();
            var value2 = ConfigManagerFactory.Instance.GetAll()
                .Where(x => ConfigManager.IsInSection(x.Key, SectionName))
                .ToDictionary(
                    x => string.IsNullOrEmpty(SectionName) ? x.Key : x.Key.Substring(SectionName.Length + 1),
                    x => x.Value
                );
            IsDirty = JsonSerializer.Serialize(value1) != JsonSerializer.Serialize(value2);
        }

        public virtual Dictionary<string, object> Serialize()
        {
            var ret = new Dictionary<string, object>();
            this.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(ConfigJsonValueAttribute), false).Length > 0)
                .ToList()
                .ForEach(p =>
                {
                    var value = p.GetValue(this);
                    ret[p.Name] = value;
                });
            return ret;
        }

        public virtual void Deserialize(IReadOnlyDictionary<string, object> dict)
        {
            this.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(ConfigJsonValueAttribute), false).Length > 0)
                .ToList()
                .ForEach(p =>
                {
                    if (!dict.ContainsKey(p.Name)) return;
                    var value = dict[p.Name];
                    var type = p.PropertyType;
                    p.SetValue(this, Convert.ChangeType(value, type));
                });
        }

        public void Reset()
        {
            if (IsModified)
            {
                ConfigManagerFactory.Instance.Reset();
                Load();
            }

            UpdateModifyStatus();
            UpdateDirtyStatus();
        }

        public void Load()
        {
            var dict = ConfigManagerFactory.Instance.GetAll();
            Deserialize(
                dict.Where(x => ConfigManager.IsInSection(x.Key, SectionName))
                    .ToDictionary(
                        x => string.IsNullOrEmpty(SectionName) ? x.Key : x.Key.Substring(SectionName.Length + 1),
                        x => x.Value
                    )
            );
        }

        public void Apply()
        {
            var dict = Serialize();
            ConfigManagerFactory.Instance.BatchApply(dict.ToDictionary(
                x => (SectionName != "" ? $"{SectionName}." : "") + x.Key,
                x => x.Value
            ));
            UpdateModifyStatus();
            UpdateDirtyStatus();
        }

        public void Save()
        {
            try
            {
                ConfigManagerFactory.Instance.Save();
            }
            catch
            {
            }

            UpdateModifyStatus();
            UpdateDirtyStatus();
        }

        public ConfigViewModelBase()
        {
            Load();
            this.PropertyChanged += (sender, args) =>
            {
                var propName = args.PropertyName;
                var type = sender.GetType();

                if (sender.GetType().GetProperty(propName)
                    .GetCustomAttributes(false)
                    .Any(u => u.GetType() == typeof(ConfigJsonValueAttribute)))
                {
                    UpdateDirtyStatus();
                }
            };
        }
    }

    public class ConfigViewModel
    {
        public GeneralConfigViewModel GeneralConfig { get; } = new GeneralConfigViewModel();
        public AppearanceConfigViewModel AppearanceConfig { get; } = new AppearanceConfigViewModel();
        public AudioConfigViewModel AudioConfig { get; } = new AudioConfigViewModel();
        public RecognizeConfigViewModel RecognizeConfig { get; } = new RecognizeConfigViewModel();

        [Reactive]
        public int CurrentTab { get; set; } = 0;

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

        private const int TAB_GENERAL = 0;
        private const int TAB_APPEARANCE = 1;
        private const int TAB_AUDIO = 2;
        private const int TAB_RECOGNIZE = 3;
        private const int TAB_ABOUT = 4;

        private ConfigViewModelBase? TabToConfig(int tab)
        {
            return tab switch
            {
                TAB_GENERAL => GeneralConfig,
                TAB_APPEARANCE => AppearanceConfig,
                TAB_AUDIO => AudioConfig,
                TAB_RECOGNIZE => RecognizeConfig,
                _ => null
            };
        }

        private ConfigViewModelBase? CurrentConfig => TabToConfig(CurrentTab);

        public ConfigViewModel()
        {
            var totalDirty = this.WhenAnyValue(
                x => x.GeneralConfig.IsDirty,
                x => x.AppearanceConfig.IsDirty,
                x => x.AudioConfig.IsDirty,
                x => x.RecognizeConfig.IsDirty
            ).Select(x => x.Item1 || x.Item2 || x.Item3 || x.Item4);

            var totalModified = this.WhenAnyValue(
                x => x.GeneralConfig.IsModified,
                x => x.AppearanceConfig.IsModified,
                x => x.AudioConfig.IsModified,
                x => x.RecognizeConfig.IsModified
            ).Select(x => x.Item1 || x.Item2 || x.Item3 || x.Item4);

            this.SaveCommand = ReactiveCommand.Create(() => { CurrentConfig?.Save(); }, totalModified);
            this.CancelCommand = ReactiveCommand.Create(() => { CurrentConfig?.Reset(); });
            this.ApplyCommand = ReactiveCommand.Create(() => { CurrentConfig?.Apply(); }, totalDirty);
        }
    }

    public class GeneralConfigViewModel : ConfigViewModelBase
    {
        protected override string SectionName => "general";

        [Reactive]
        [ConfigJsonValue]
        public string Language { get; set; } = "zh-cn";

        public ObservableCollection<KeyValuePair<string, string>> LanguagesAvailable { get; } =
        [
            new KeyValuePair<string, string>("zh-cn", "简体中文"),
            new KeyValuePair<string, string>("en-us", "English"),
        ];

        [Reactive]
        [ConfigJsonValue]
        public string UserDir { get; set; } = "D:\\TMSpeech";

        [Reactive]
        [ConfigJsonValue]
        public bool LaunchOnStartup { get; set; } = false;

        [Reactive]
        [ConfigJsonValue]
        public bool StartOnLaunch { get; set; } = false;

        [Reactive]
        [ConfigJsonValue]
        public bool AutoUpdate { get; set; } = true;
    }

    public class AppearanceConfigViewModel : ConfigViewModelBase
    {
        protected override string SectionName => "appearance";

        public List<FontFamily> FontsAvailable { get; private set; }

        [Reactive]
        [ConfigJsonValue]
        public uint ShadowColor { get; set; } = 0xFF000000;


        [Reactive]
        [ConfigJsonValue]
        public int ShadowSize { get; set; } = 10;


        [Reactive]
        [ConfigJsonValue]
        public string FontFamily { get; set; } = "Arial";

        [Reactive]
        [ConfigJsonValue]
        public int FontSize { get; set; } = 24;

        [Reactive]
        [ConfigJsonValue]
        public uint FontColor { get; set; } = 0xFFFF0000;

        [Reactive]
        [ConfigJsonValue]
        public uint MouseHover { get; set; } = 0xFFFF0000;

        [Reactive]
        [ConfigJsonValue]
        public int TextAlign { get; set; } = TextAlignEnum.Left;

        public static class TextAlignEnum
        {
            public const int Left = 0;
            public const int Center = 1;
            public const int Right = 2;
            public const int Justify = 3;
        }

        public List<KeyValuePair<int, string>> TextAligns { get; } =
        [
            new KeyValuePair<int, string>(TextAlignEnum.Left, "左对齐"),
            new KeyValuePair<int, string>(TextAlignEnum.Center, "居中对齐"),
            new KeyValuePair<int, string>(TextAlignEnum.Right, "右对齐"),
            new KeyValuePair<int, string>(TextAlignEnum.Justify, "两端对齐"),
        ];

        public AppearanceConfigViewModel()
        {
            FontsAvailable = FontManager.Current.SystemFonts.ToList();
        }
    }

    public class AudioConfigViewModel : ConfigViewModelBase
    {
        protected override string SectionName => "audio";

        [Reactive]
        [ConfigJsonValue]
        public string AudioSource { get; set; } = "";

        [ObservableAsProperty]
        public IReadOnlyList<Core.Plugins.IAudioSource> AudioSourcesAvailable { get; } =
            new List<Core.Plugins.IAudioSource>();

        [ObservableAsProperty]
        public IPluginConfiguration? Config { get; }

        [Reactive]
        [ConfigJsonValue]
        public string PluginConfig { get; set; } = "";

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public IReadOnlyList<Core.Plugins.IAudioSource> Refresh()
        {
            var plugins = Core.Plugins.PluginManagerFactory.GetInstance().AudioSources;
            if (AudioSource == "" && plugins.Count >= 1)
                AudioSource = plugins[0].Name;
            return plugins;
        }

        public AudioConfigViewModel()
        {
            this.RefreshCommand = ReactiveCommand.Create(() => { });
            this.RefreshCommand.Merge(Observable.Return(Unit.Default))
                .SelectMany(u => Observable.FromAsync(() => Task.Run(() => Refresh())))
                .ToPropertyEx(this, x => x.AudioSourcesAvailable);

            this.WhenAnyValue(u => u.AudioSource, u => u.AudioSourcesAvailable)
                .Where((u) => u.Item1 != null && u.Item2 != null)
                .Select(u => u.Item1)
                .Select(x => AudioSourcesAvailable.FirstOrDefault(u => u.Name == x))
                .Select(x => x?.Configuration)
                .ToPropertyEx(this, x => x.Config);
        }
    }


    public class RecognizeConfigViewModel : ConfigViewModelBase
    {
        protected override string SectionName => "recognize";

        [Reactive]
        [ConfigJsonValue]
        public string Recognizer { get; set; } = "";

        [ObservableAsProperty]
        public IReadOnlyList<Core.Plugins.IRecognizer> RecognizersAvailable { get; } =
            new List<Core.Plugins.IRecognizer>();

        [ObservableAsProperty]
        public IPluginConfiguration? Config { get; }

        [Reactive]
        [ConfigJsonValue]
        public string PluginConfig { get; set; } = "";

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public IReadOnlyList<Core.Plugins.IRecognizer> Refresh()
        {
            var plugins = Core.Plugins.PluginManagerFactory.GetInstance().Recognizers;
            if (Recognizer == "" && plugins.Count >= 1)
                Recognizer = plugins[0].Name;
            return plugins;
        }

        public RecognizeConfigViewModel()
        {
            this.RefreshCommand = ReactiveCommand.Create(() => { });
            this.RefreshCommand.Merge(Observable.Return(Unit.Default))
                .SelectMany(u => Observable.FromAsync(() => Task.Run(() => Refresh())))
                .ToPropertyEx(this, x => x.RecognizersAvailable);

            this.WhenAnyValue(u => u.Recognizer, u => u.RecognizersAvailable)
                .Where((u) => u.Item1 != null && u.Item2 != null)
                .Select(u => u.Item1)
                .Select(x => RecognizersAvailable.FirstOrDefault(u => u.Name == x))
                .Select(x => x?.Configuration)
                .ToPropertyEx(this, x => x.Config);
        }
    }
}