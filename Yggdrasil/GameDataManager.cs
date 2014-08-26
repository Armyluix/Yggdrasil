﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;

using Yggdrasil.Forms;
using Yggdrasil.FileHandling;
using Yggdrasil.FileHandling.TableHandling;
using Yggdrasil.Helpers;
using Yggdrasil.TableParsing;
using Yggdrasil.Attributes;

namespace Yggdrasil
{
    public class GameDataManager
    {
        static readonly string[] messageDirs = new string[]
        {
            "data\\Data\\CharaSel", "data\\Data\\Dungeon", "data\\Data\\Event", "data\\Data\\Opening", "data\\Data\\Param", "data\\Data\\SaveLoad", "data\\Data\\Battle"
        };

        static readonly string[] dataDirs = new string[]
        {
            "data\\Data\\Param", "data\\Data\\Battle", "data\\Data\\Event"
        };

        static readonly string ItemNameFile = "ItemName";
        static readonly string ItemInfoFile = "ItemInfo";
        static readonly string EnemyNameFile = "EnemyName";
        static readonly string EnemyInfoFile = "EnemyInfo";
        static readonly string PlayerSkillNameFile = "PlayerSkillName";
        static readonly string CampSkillInfoFile = "CampSkillInfo";
        static readonly string CampSkillExeInfoFile = "CampSkillExeInfo";

        public enum Versions { Invalid, European, American, Japanese };
        public enum Languages { English, German, Spanish, French, Italian };

        public string DataPath { get; private set; }

        public HeaderFile Header { get; private set; }
        public Versions Version { get; private set; }

        Languages language;
        public Languages Language
        {
            get { return language; }
            set
            {
                language = value;
                var handler = SelectedLanguageChangedEvent;
                if (handler != null) handler(this, new EventArgs());
            }
        }
        public event EventHandler SelectedLanguageChangedEvent;

        string mainFontFilename;
        Dictionary<Languages, string> langSuffixes = new Dictionary<Languages, string>()
        {
            { Languages.German, "_DE" },
            { Languages.English, "_EN" },
            { Languages.Spanish, "_ES" },
            { Languages.French, "_FR" },
            { Languages.Italian, "_IT" }
        };

        DataLoadWaitForm loadWaitForm;
        BackgroundWorker loadWaitWorker;

        public bool IsInitialized { get; private set; }

        public FontRenderer FontRenderer { get; private set; }

        public List<TableFile> MessageFiles { get; private set; }
        List<TableFile> changedMessageFiles;
        public bool MessageFileHasChanged { get { return (changedMessageFiles != null && changedMessageFiles.Count > 0); } }
        public int ChangedMessageFileCount { get { return (changedMessageFiles != null ? changedMessageFiles.Count : -1); } }

        List<TableFile> dataTableFiles;
        List<BaseParser> parsedData;

        List<BaseParser> changedParsedData;
        public bool DataHasChanged { get { return (changedParsedData != null && changedParsedData.Count > 0); } }
        public int ChangedDataCount { get { return (changedParsedData != null ? changedParsedData.Count : -1); } }
        public event PropertyChangedEventHandler ItemDataPropertyChangedEvent;

        public static Dictionary<ushort, string> ItemNames { get; private set; }
        public static Dictionary<ushort, string> EnemyNames { get; private set; }
        public static Dictionary<ushort, string> EncounterDescriptions { get; private set; }
        public static Dictionary<ushort, string> PlayerSkillNames { get; private set; }
        public static List<string> AINames { get; private set; }
        public static List<string> SpriteNames { get; private set; }

        public GameDataManager() { }

        public void ReadGameDirectory(string path)
        {
            DataPath = path;
            IsInitialized = false;

            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            loadWaitWorker = new BackgroundWorker();
            loadWaitWorker.WorkerReportsProgress = true;
            loadWaitWorker.DoWork += ((s, e) =>
            {
                try
                {
                    System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

                    loadWaitWorker.ReportProgress(-1, "Reading game directory...");
                    ReadHeaderIdentify();

                    PrepareDirectoryUnpack();

                    loadWaitWorker.ReportProgress(-1, "Generating character map...");
                    EtrianString.GameVersion = Version;

                    loadWaitWorker.ReportProgress(-1, "Initializing font renderer...");
                    FontRenderer = new FontRenderer(this, Path.Combine(path, mainFontFilename));

                    MessageFiles = ReadDataTablesByExtension(".mbb", messageDirs);
                    dataTableFiles = ReadDataTablesByExtension(".tbb", dataDirs);

                    EnsureMessageTableIntegrity();

                    changedMessageFiles = new List<TableFile>();

                    parsedData = ParseDataTables();
                    changedParsedData = new List<BaseParser>();

                    GenerateDictionariesLists();

                    IsInitialized = true;
                }
                catch (Exceptions.GameDataManagerException gameException)
                {
                    MessageBox.Show(
                        string.Format("{0}{1}{1}{2}", gameException.Message, Environment.NewLine, "Please ensure you've selected a valid game data directory."), "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
#if !DEBUG
                catch (Exception exception)
                {
                    MessageBox.Show(
                        string.Format("{0} occured: {1}{2}{2}{3}", exception.GetType().FullName, exception.Message, Environment.NewLine, "Please contact a developer about this message."), "Exception",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
#endif
            });
            loadWaitWorker.ProgressChanged += ((s, e) =>
            {
                loadWaitForm.PrintStatus(e.UserState as string);
                Program.Logger.LogMessage(e.UserState as string);
            });
            loadWaitWorker.RunWorkerCompleted += ((s, e) =>
            {
                loadWaitForm.Close();
                stopwatch.Stop();
                Program.Logger.LogMessage("Game directory read in {0:0.000} sec...", stopwatch.Elapsed.TotalSeconds);
            });
            loadWaitWorker.RunWorkerAsync();

            loadWaitForm = new DataLoadWaitForm();
            loadWaitForm.ShowDialog(Program.MainForm);
        }

        public int SaveAllChanges()
        {
            if (parsedData == null || changedParsedData == null) return 0;

            List<TableFile> changedFiles = new List<TableFile>();

            foreach (BaseParser data in changedParsedData)
            {
                data.Save();
                if (!changedFiles.Contains(data.ParentTable.TableFile)) changedFiles.Add(data.ParentTable.TableFile);
            }

            changedFiles.AddRange(changedMessageFiles);

            foreach (TableFile file in changedFiles)
            {
                if (!file.IsCompressed)
                {
                    file.Save();

                    BinaryWriter writer = new BinaryWriter(File.Open(file.Filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                    writer.Write(file.Data);
                    writer.Close();
                }
                else
                    throw new NotImplementedException("Saving to compressed file not implemented");
            }

            changedParsedData.Clear();
            changedMessageFiles.Clear();

            return changedFiles.Count();
        }

        private void ReadHeaderIdentify()
        {
            loadWaitWorker.ReportProgress(-1, "Reading header data...");

            if (!File.Exists(Path.Combine(DataPath, "header.bin"))) throw new Exceptions.GameDataManagerException("File header.bin not found.");

            Header = new FileHandling.HeaderFile(this, Path.Combine(DataPath, "header.bin"));
            switch (Header.GameCode)
            {
                case "AKYP":
                    Version = Versions.European;
                    mainFontFilename = "data\\Data\\Tex\\Font\\Font14x11_00.cmp";
                    break;

                case "AKYE":
                    Version = Versions.American;
                    mainFontFilename = "data\\Data\\Tex\\Font\\Font10x5_00.cmp";
                    break;

                case "AKYJ":
                    Version = Versions.Japanese;
                    mainFontFilename = "data\\Data\\Tex\\Font\\Font10x10_00.cmp";
                    break;

                default: throw new Exceptions.GameDataManagerException("Unsupported game data.");
            }

            Program.Logger.LogMessage("Identified game '{0} {1}' as {2} version.", Header.GameTitle, Header.GameCode, Version);
        }

        private void PrepareDirectoryUnpack()
        {
            /* Do we need to decompress anything? */
            string checkPath = Path.Combine(DataPath, "data\\Data\\Event", "DUN_01F.evt");
            bool needToUnpack = !File.Exists(checkPath);

            if (needToUnpack)
            {
                loadWaitWorker.ReportProgress(-1, "Preparing to decompress files...");

                List<Tuple<string, string, string, bool, bool>> dirExtTuples = new List<Tuple<string, string, string, bool, bool>>
                { 
                    /* Path, Original Name/Ext, New Name/Ext, Localized?, ARM9-Autofix? */
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Event", ".cmp", ".evt", false, false),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\MapDat", "_ydd.cmp", ".ydd", false, false),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\MapDat", "_ymd.cmp", ".ymd", false, false),

                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "BarQuestData.cmp", "BarQuestData.tbb", false, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "BarQuestMess.cmp", "BarQuestMess.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "BarQuestName.cmp", "BarQuestName.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "BtlItemInfo.cmp", "BtlItemInfo.tbb", false, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "CampText.cmp", "CampText.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "FacilityText.cmp", "FacilityText.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "GovernmentMissionData.cmp", "GovernmentMissionData.tbb", false, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "GovernmentMissionMess.cmp", "GovernmentMissionMess.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "GovernmentMissionName.cmp", "GovernmentMissionName.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "GovernmentMissionPrize.cmp", "GovernmentMissionPrize.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "ItemChoiceInfo.cmp", "ItemChoiceInfo.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "itemIllInfo.cmp", "itemIllInfo.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "ItemInfo.cmp", "ItemInfo.mbb", true, true),
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Param", "ItemName.cmp", "ItemName.mbb", true, true),
                    
                    new Tuple<string, string, string, bool, bool>("data\\Data\\Battle", "BtlMess.cmp", "BtlMess.mbb", true, true),
                };

                List<Tuple<string, string, string, bool, bool>> dirExtTuplesLocalized = new List<Tuple<string, string, string, bool, bool>>();

                if (Version == Versions.European)
                {
                    dirExtTuplesLocalized.AddRange(dirExtTuples.Where(x => !x.Item4));

                    foreach (Tuple<string, string, string, bool, bool> dirExt in dirExtTuples.Where(x => x.Item4))
                    {
                        int sourcePeriodIdx = dirExt.Item2.IndexOf('.');
                        string sourceFileName = dirExt.Item2.Substring(0, sourcePeriodIdx);
                        string sourceFileExt = dirExt.Item2.Substring(sourcePeriodIdx);

                        int destPeriodIdx = dirExt.Item3.IndexOf('.');
                        string destFileName = dirExt.Item3.Substring(0, sourcePeriodIdx);
                        string destFileExt = dirExt.Item3.Substring(sourcePeriodIdx);

                        foreach (KeyValuePair<Languages, string> langSuffix in langSuffixes)
                        {
                            dirExtTuplesLocalized.Add(new Tuple<string, string, string, bool, bool>(
                                dirExt.Item1,
                                string.Format("{0}{1}{2}", sourceFileName, langSuffix.Value, sourceFileExt),
                                string.Format("{0}{1}{2}", destFileName, langSuffix.Value, destFileExt),
                                false,
                                dirExt.Item5));
                        }
                    }
                }

                /* Find and decompress files */
                foreach (Tuple<string, string, string, bool, bool> dirExt in (Version == Versions.European ? dirExtTuplesLocalized : dirExtTuples))
                {
                    string localDataPath = Path.Combine(DataPath, dirExt.Item1);
                    if (!Directory.Exists(localDataPath)) continue;

                    List<string> filePathsAll = Directory.EnumerateFiles(localDataPath, "*.*", SearchOption.AllDirectories).ToList();
                    List<string> filePaths = filePathsAll
                        .Where(x => x.ToLowerInvariant().EndsWith(dirExt.Item2.ToLowerInvariant()) || Path.GetFileName(x.ToLowerInvariant()) == dirExt.Item2.ToLowerInvariant())
                        .ToList();

                    foreach (string filePath in filePaths)
                    {
                        loadWaitWorker.ReportProgress(-1, string.Format("Decompressing {0}...", Path.GetFileName(filePath)));

                        string newPath = Path.Combine(Path.GetDirectoryName(filePath), filePath.Replace(dirExt.Item2, dirExt.Item3));

                        bool isCompressed;
                        byte[] fileData = DataCompression.Decompressor.Decompress(filePath, out isCompressed);
                        if (isCompressed)
                        {
                            BinaryWriter writer = new BinaryWriter(File.Create(newPath));
                            writer.Write(fileData);
                            writer.Close();

                            File.Delete(filePath);
                        }
                    }
                }

                /* Read ARM9 binary */
                BinaryReader arm9Reader = new BinaryReader(File.Open(Path.Combine(DataPath, "arm9.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                byte[] arm9Data = new byte[arm9Reader.BaseStream.Length];
                arm9Reader.Read(arm9Data, 0, arm9Data.Length);
                arm9Reader.Close();

                /* Apply ARM9 patches */
                loadWaitWorker.ReportProgress(-1, "Patching ARM9 binary...");

                bool eventsPatched = false, mapsPatched = false;
                for (int i = 0; i < arm9Data.Length; i += 4)
                {
                    string check = string.Empty;

                    /* Event data */
                    if (!eventsPatched && i < arm9Data.Length - 8)
                    {
                        check = Encoding.ASCII.GetString(arm9Data, i, 8);
                        if (check == "MIS_\0\0\0\0")
                        {
                            Buffer.BlockCopy(Encoding.ASCII.GetBytes(".evt"), 0, arm9Data, i + 0x1C, 4);
                            eventsPatched = true;
                        }
                    }

                    /* Map data */
                    if (!mapsPatched && i < arm9Data.Length - 8)
                    {
                        check = Encoding.ASCII.GetString(arm9Data, i, 8);
                        if (check == "_ymd.cmp")
                        {
                            Buffer.BlockCopy(Encoding.ASCII.GetBytes(".ymd\0\0\0\0"), 0, arm9Data, i, 8);
                            Buffer.BlockCopy(Encoding.ASCII.GetBytes(".ydd\0\0\0\0"), 0, arm9Data, i + 0xC, 8);
                            mapsPatched = true;
                        }
                    }
                }

                /* Data/message tables */
                foreach (Tuple<string, string, string, bool, bool> e in dirExtTuples.Where(x => x.Item5))
                {
                    string originalData = e.Item1.Substring(e.Item1.IndexOf('\\') + 1).Replace('\\', '/') + "/" + e.Item2;
                    string replacedData = e.Item1.Substring(e.Item1.IndexOf('\\') + 1).Replace('\\', '/') + "/" + e.Item3;

                    for (int i = 0; i < arm9Data.Length; i += 4)
                    {
                        if (i < arm9Data.Length - originalData.Length)
                        {
                            string check = Encoding.ASCII.GetString(arm9Data, i, originalData.Length);
                            if (check.StartsWith(originalData)) Buffer.BlockCopy(Encoding.ASCII.GetBytes(replacedData), 0, arm9Data, i, replacedData.Length);
                        }
                    }
                }

                /* Write patched ARM9 binary */
                BinaryWriter arm9Writer = new BinaryWriter(File.Open(Path.Combine(DataPath, "arm9.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                arm9Writer.Write(arm9Data);
                arm9Writer.Close();
            }
        }

        private List<TableFile> ReadDataTablesByExtension(string extension, string[] directories)
        {
            if (extension == null || extension == string.Empty) throw new ArgumentException("No extension given");
            if (directories == null) throw new ArgumentNullException("Directories is null");

            List<TableFile> dataTables = new List<TableFile>();

            foreach (string directory in directories)
            {
                string localDataPath = Path.Combine(DataPath, directory);
                if (!Directory.Exists(localDataPath)) continue;

                List<string> filePaths = Directory.EnumerateFiles(localDataPath, "*.*", SearchOption.AllDirectories)
                    .Where(x => x.ToLowerInvariant().EndsWith(extension) || x.ToLowerInvariant().EndsWith(".cmp"))
                    .ToList();

                foreach (string filePath in filePaths)
                {
                    TableFile tbb = new TableFile(this, filePath);
                    /*if (tbb.IsValid())*/
                    dataTables.Add(tbb);

                    loadWaitWorker.ReportProgress(-1, string.Format("Reading {0}...", Path.GetFileName(filePath)));
                }
            }

            return dataTables;
        }

        private void EnsureMessageTableIntegrity()
        {
            if (MessageFiles == null) return;

            List<TableFile> filesToRemove = new List<TableFile>();
            foreach (TableFile messageFile in MessageFiles)
            {
                bool removeFile = true;
                foreach (BaseTable table in messageFile.Tables)
                {
                    if (table is MessageTable) removeFile = false;
                }

                if (removeFile) filesToRemove.Add(messageFile);
            }

            foreach (TableFile file in filesToRemove) MessageFiles.Remove(file);
        }

        private List<BaseParser> ParseDataTables()
        {
            List<BaseParser> parsedData = new List<BaseParser>();

            List<Type> typesWithAttrib = Assembly.GetExecutingAssembly().GetTypes().Where(x => x.GetCustomAttributes(typeof(ParserUsage), false).Length > 0).ToList();
            foreach (Type type in typesWithAttrib)
            {
                foreach (ParserUsage attrib in type.GetCustomAttributes(false).Where(x => x is ParserUsage))
                {
                    loadWaitWorker.ReportProgress(-1, string.Format("Parsing {0}...", attrib.FileName));

                    TableFile tableFile =
                        dataTableFiles.FirstOrDefault(x => Path.GetFileName(x.Filename) == attrib.FileName || Path.GetFileNameWithoutExtension(x.Filename) == Path.GetFileNameWithoutExtension(attrib.FileName));

                    if (tableFile == null)
                        throw new FileNotFoundException(string.Format("Table file {0} not found in loaded files", attrib.FileName));

                    if (attrib.TableNo >= tableFile.NumTables)
                        throw new ArgumentException(string.Format("Table number {0} does not exist in file", attrib.TableNo));

                    if (!(tableFile.Tables[attrib.TableNo] is DataTable))
                        throw new InvalidCastException(string.Format("Table number {0} is of wrong type {1}", attrib.TableNo, tableFile.Tables[attrib.TableNo].GetType().Name));

                    DataTable table = (tableFile.Tables[attrib.TableNo] as DataTable);
                    for (int i = 0; i < table.Data.Length; i++)
                        parsedData.Add((BaseParser)Activator.CreateInstance(type, new object[] { this, table, i, (PropertyChangedEventHandler)ItemDataPropertyChanged }));
                }
            }

            return parsedData;
        }

        private void GenerateDictionariesLists()
        {
            loadWaitWorker.ReportProgress(-1, "Generating various dictionaries...");

            FetchItemNames();
            FetchEnemyNames();
            FetchEncounterDescriptions();
            FetchPlayerSkillNames();

            SpriteNames = Directory.EnumerateFiles(Path.Combine(DataPath, "data\\Data\\Tex\\Enemy"), "*.*", SearchOption.AllDirectories).Select(x => Path.GetFileNameWithoutExtension(x).Substring(3)).ToList();
            AINames = Directory.EnumerateFiles(Path.Combine(DataPath, "data\\Data\\Battle"), "ai_*.tbb", SearchOption.AllDirectories).Select(x => Path.GetFileNameWithoutExtension(x).Substring(3)).ToList();
        }

        private void FetchItemNames()
        {
            ItemNames = new Dictionary<ushort, string>();
            ItemNames.Add(0, "(None)");
            foreach (BaseItemParser parser in parsedData.Where(x => (x is EquipItemParser || x is MiscItemParser))) ItemNames.Add(parser.ItemNumber, parser.Name);
        }

        private void FetchEnemyNames()
        {
            EnemyNames = new Dictionary<ushort, string>();
            EnemyNames.Add(0, "(None)");
            foreach (EnemyDataParser parser in parsedData.Where(x => (x is EnemyDataParser))) if (parser.EnemyNumber != 0) EnemyNames.Add(parser.EnemyNumber, parser.Name);
        }

        private void FetchEncounterDescriptions()
        {
            EncounterDescriptions = new Dictionary<ushort, string>();
            foreach (EncounterParser parser in parsedData.Where(x => (x is EncounterParser))) EncounterDescriptions.Add(parser.EncounterNumber, parser.EntryDescription);
        }

        private void FetchPlayerSkillNames()
        {
            PlayerSkillNames = new Dictionary<ushort, string>();
            PlayerSkillNames.Add(0, "(None)");
            foreach (PlayerSkillReqParser parser in parsedData.Where(x => (x is PlayerSkillReqParser))) PlayerSkillNames.Add(parser.SkillNumber, parser.Name);
        }

        public string GetItemName(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(ItemNameFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is BaseItemParser && (x as BaseItemParser).ItemNumber == number) as BaseItemParser).Name;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetItemName(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(ItemNameFile, 0, number - 1) == string.Empty) return;
            SetMessageString(ItemNameFile, 0, number - 1, message);
        }

        public string GetItemDescription(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(ItemInfoFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is BaseItemParser && (x as BaseItemParser).ItemNumber == number) as BaseItemParser).Description;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetItemDescription(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(ItemInfoFile, 0, number - 1) == string.Empty) return;
            SetMessageString(ItemInfoFile, 0, number - 1, message);
        }

        public string GetEnemyName(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(EnemyNameFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is EnemyDataParser && (x as EnemyDataParser).EnemyNumber == number) as EnemyDataParser).Name;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetEnemyName(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(EnemyNameFile, 0, number - 1) == string.Empty) return;
            SetMessageString(EnemyNameFile, 0, number - 1, message);
        }

        public string GetEnemyDescription(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(EnemyInfoFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is EnemyDataParser && (x as EnemyDataParser).EnemyNumber == number) as EnemyDataParser).Description;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetEnemyDescription(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(EnemyInfoFile, 0, number - 1) == string.Empty) return;
            SetMessageString(EnemyInfoFile, 0, number - 1, message);
        }

        public string GetPlayerSkillName(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(PlayerSkillNameFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is PlayerSkillReqParser && (x as PlayerSkillReqParser).SkillNumber == number) as PlayerSkillReqParser).Name;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetPlayerSkillName(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(PlayerSkillNameFile, 0, number - 1) == string.Empty) return;
            SetMessageString(PlayerSkillNameFile, 0, number - 1, message);
        }

        public string GetPlayerSkillShortDescription(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(CampSkillExeInfoFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is PlayerSkillReqParser && (x as PlayerSkillReqParser).SkillNumber == number) as PlayerSkillReqParser).Description;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetPlayerSkillShortDescription(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(CampSkillExeInfoFile, 0, number - 1) == string.Empty) return;
            SetMessageString(CampSkillExeInfoFile, 0, number - 1, message);
        }

        public string GetPlayerSkillDescription(ushort number)
        {
            if ((number - 1) < 0) return "(Unnamed)";

            string value = string.Empty;
            if (parsedData == null) value = GetMessageString(CampSkillInfoFile, 0, number - 1);
            else value = (parsedData.FirstOrDefault(x => x is PlayerSkillReqParser && (x as PlayerSkillReqParser).SkillNumber == number) as PlayerSkillReqParser).Description;

            return (value != string.Empty ? value : "(Unnamed)");
        }

        public void SetPlayerSkillDescription(ushort number, EtrianString message)
        {
            if ((number - 1) < 0 || GetMessageString(CampSkillInfoFile, 0, number - 1) == string.Empty) return;
            SetMessageString(CampSkillInfoFile, 0, number - 1, message);
        }

        private void ItemDataPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            changedParsedData = parsedData.Where(x => x.HasChanged).ToList();

            if (sender is BaseItemParser) FetchItemNames();

            if (sender is EnemyDataParser)
            {
                FetchEnemyNames();
                FetchEncounterDescriptions();
            }

            if (sender is PlayerSkillReqParser) FetchPlayerSkillNames();

            var handler = ItemDataPropertyChangedEvent;
            if (handler != null) handler(sender, e);
            return;

            System.Windows.Forms.MessageBox.Show(string.Format("Property {0} in {1} (0x{2:X}) changed; new value is {3} (0x{3:X})",
                e.PropertyName, sender.GetType().Name, sender.GetHashCode(), sender.GetProperty(e.PropertyName)));
        }

        public TableFile GetMessageFile(string filename)
        {
            filename = (Version == Versions.European ? string.Format("{0}{1}", filename, langSuffixes[Language]) : filename);
            TableFile messageFile = MessageFiles.FirstOrDefault(x => x.Filename != null && Path.GetFileName(x.Filename).StartsWith(filename));

            if (messageFile == null) throw new ArgumentException("Message file could not be found");
            return messageFile;
        }

        public EtrianString GetMessageString(string filename, int tableNo, int messageNo)
        {
            TableFile messageFile = GetMessageFile(filename);
            return (messageFile.Tables[tableNo] as MessageTable).Messages[messageNo];
        }

        public void SetMessageString(string filename, int tableNo, int messageNo, EtrianString message)
        {
            TableFile messageFile = GetMessageFile(filename);
            (messageFile.Tables[tableNo] as MessageTable).Messages[messageNo] = message;
            if (!changedMessageFiles.Contains(messageFile)) changedMessageFiles.Add(messageFile);
        }

        public IList<T> GetParsedData<T>()
        {
            return parsedData.Where(x => x is T).Cast<T>().ToList();
        }

        public IList<Tuple<Type, IList<BaseParser>>> GetAllParsedData(bool mustSupportSave)
        {
            List<Tuple<Type, IList<BaseParser>>> output = new List<Tuple<Type, IList<BaseParser>>>();

            List<Type> typesWithAttrib = Assembly.GetExecutingAssembly().GetTypes().Where(x => x.GetCustomAttributes(typeof(ParserUsage), false).Length > 0).ToList();
            foreach (Type type in typesWithAttrib.OrderBy(x => ((PrioritizedDescription)x.GetAttribute<PrioritizedDescription>()).Priority))
            {
                if (mustSupportSave)
                {
                    MethodInfo mi = type.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public);
                    if (mi.DeclaringType == typeof(BaseParser)) continue;
                }

                output.Add(new Tuple<Type, IList<BaseParser>>(type, parsedData.Where(x => x.GetType() == type).ToList()));
            }

            return output;
        }
    }
}
