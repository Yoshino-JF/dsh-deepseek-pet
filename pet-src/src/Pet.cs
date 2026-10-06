// DeepSeek 桌宠（DeepSeekPet）—— 悬浮在 Windows 桌面上的透明置顶小女仆
// 纯 .NET Framework 4.x + WPF，无第三方依赖；余额与状态由 DSH 插件通过本机回环接口提供。
//
// 编译（见 build.cmd）：
//   csc /target:winexe /win32manifest:app.manifest /r:PresentationFramework.dll ...
//
// 交互：左键拖动移动 / 单击说话 / 双击开心 / 滚轮缩放 / 右键菜单
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using WinForms = System.Windows.Forms;

namespace DeepSeekPet
{
    // ---------------- 配置 ----------------
    class PetConfig
    {
        public string stateUrl = "http://127.0.0.1:47831/state";
        public double spriteWidth = 220;
        public double x = -1;
        public double y = -1;
        public bool topmost = true;
        public bool showBalance = true;
        public bool particles = true;
        public bool sway = true;
        public double opacity = 1.0;
        public int pollSeconds = 5;
        public int longPollSeconds = 25;   // >0 = 长轮询（宿主有事件立刻回，反应 <100ms）；0 = 普通轮询
        public double lowBalance = 10.0;
        public string assetsDir = "";
        public string moodOverride = "auto";   // auto | idle | happy | worry | sleepy
        public string screenMode = "auto";     // auto | on | off —— 游戏屏（Minecraft 小显示器）
        public string game = "fp";             // fp = 伪 3D 第一人称砍树；side = 2D 横版清关
        /// <summary>true = 用户手动选定过游戏画面（点显示器切换 / 菜单选）→ 进入游戏时固定用这款，不再随机</summary>
        public bool gameFixed = false;
        /// <summary>常驻气泡特效（用户要求重做）：她身侧/头顶缓缓上浮的小水泡</summary>
        public bool bubbles = true;
        public double gameIdleSeconds = 90;    // 无互动多久开始玩游戏
        public double sleepIdleSeconds = 600;  // 无互动多久趴睡
        public double creeperChance = 0.08;    // 砍倒一棵树后被苦力怕炸的概率
        public bool screenOnly = false;        // 挂机模式：只留纯代码游戏屏，把她收起来（v0.2.0）

        static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "deepseek-pet");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string PathFile { get { return Path.Combine(Dir, "config.json"); } }
        public static string PathCache { get { return Path.Combine(Dir, "last-state.json"); } }

        public static PetConfig Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    string txt = File.ReadAllText(PathFile, Encoding.UTF8);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(txt);
                    PetConfig c = new PetConfig();
                    c.stateUrl = Str(d, "stateUrl", c.stateUrl);
                    c.spriteWidth = Num(d, "spriteWidth", c.spriteWidth);
                    c.x = Num(d, "x", c.x);
                    c.y = Num(d, "y", c.y);
                    c.topmost = Bool(d, "topmost", c.topmost);
                    c.showBalance = Bool(d, "showBalance", c.showBalance);
                    c.particles = Bool(d, "particles", c.particles);
                    c.sway = Bool(d, "sway", c.sway);
                    c.opacity = Num(d, "opacity", c.opacity);
                    c.pollSeconds = (int)Num(d, "pollSeconds", c.pollSeconds);
                    c.longPollSeconds = (int)Num(d, "longPollSeconds", c.longPollSeconds);
                    c.lowBalance = Num(d, "lowBalance", c.lowBalance);
                    c.assetsDir = Str(d, "assetsDir", c.assetsDir);
                    c.moodOverride = Str(d, "moodOverride", c.moodOverride);
                    c.screenMode = Str(d, "screenMode", c.screenMode);
                    c.game = Str(d, "game", c.game);
            c.gameFixed = Bool(d, "gameFixed", c.gameFixed);
            c.bubbles = Bool(d, "bubbles", c.bubbles);
                    c.gameIdleSeconds = Num(d, "gameIdleSeconds", c.gameIdleSeconds);
                    c.sleepIdleSeconds = Num(d, "sleepIdleSeconds", c.sleepIdleSeconds);
                    c.creeperChance = Num(d, "creeperChance", c.creeperChance);
                    c.screenOnly = Bool(d, "screenOnly", c.screenOnly); // 挂机模式
                    return c;
                }
            }
            catch (Exception ex) { Log("load config failed: " + ex.Message); }
            return new PetConfig();
        }

        public void Save()
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["stateUrl"] = stateUrl; d["spriteWidth"] = spriteWidth; d["x"] = x; d["y"] = y;
                d["topmost"] = topmost; d["showBalance"] = showBalance; d["particles"] = particles;
                d["sway"] = sway; d["opacity"] = opacity; d["pollSeconds"] = pollSeconds;
                d["longPollSeconds"] = longPollSeconds;
                d["lowBalance"] = lowBalance; d["assetsDir"] = assetsDir; d["moodOverride"] = moodOverride;
                d["screenMode"] = screenMode; d["game"] = game; d["gameFixed"] = gameFixed; d["bubbles"] = bubbles; d["gameIdleSeconds"] = gameIdleSeconds;
                d["sleepIdleSeconds"] = sleepIdleSeconds; d["creeperChance"] = creeperChance;
                d["screenOnly"] = screenOnly;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(PathFile, ser.Serialize(d), Encoding.UTF8);
            }
            catch (Exception ex) { Log("save config failed: " + ex.Message); }
        }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Dir, "pet.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        static string Str(Dictionary<string, object> d, string k, string def)
        { object v; return (d != null && d.TryGetValue(k, out v) && v != null) ? Convert.ToString(v) : def; }
        static double Num(Dictionary<string, object> d, string k, double def)
        { object v; if (d != null && d.TryGetValue(k, out v) && v != null) { double r; if (double.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out r)) return r; } return def; }
        static bool Bool(Dictionary<string, object> d, string k, bool def)
        { object v; if (d != null && d.TryGetValue(k, out v) && v != null) { bool r; if (bool.TryParse(Convert.ToString(v), out r)) return r; } return def; }
    }

    // ---------------- 余额快照 ----------------
    class PetSnapshot
    {
        public bool ok;
        public string currency = "CNY";
        public double total = double.NaN;
        public double granted;
        public double toppedUp;
        public bool isAvailable = true;
        public string message = "";
        public string command = "";
        public bool busy;
        public string activityLabel = "";
        public string updatedAt = "";
        public long eventSeq = -1;
        public string eventKind = "";
        public double lowBalance = 10;

        public string TotalText
        {
            get
            {
                if (double.IsNaN(total)) return "--";
                string sym = currency == "USD" ? "$" : "¥";
                return sym + total.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
    }

    // ---------------- 主程序 ----------------
    public class PetApp
    {
        /// <summary>桌宠版本号（与插件 @deepseekstudio/dsh-deepseek-pet 的版本保持一致，见 DEVLOG.md）</summary>
        public const string Version = "0.2.10";
        /// <summary>开发用（--vnmood love|angry）：启动即演一次满好感/生气画面，便于验收截图</summary>
        public static string VnMood = "";
        /// <summary>开发用（--sidetest）：横版屏摆一个"血少+有食物+骷髅在射程"的局面，便于验收战斗</summary>
        public static bool SideTest;
        /// <summary>开发用（--sidedeath）：启动 10 秒后强制死亡一次，验收"死亡 → 重新加载"</summary>
        public static bool SideDeath;
        /// <summary>开发用（--screenonly）：启动即进挂机模式（只留游戏屏）</summary>
        public static bool ScreenOnlyTest;
        /// <summary>开发用（--posetest &lt;pick|doze|handheld|meditate|idle&gt;）：启动即切到指定姿态，便于验收素材</summary>
        public static string PoseTest = "";
        /// <summary>开发用（--creepertest）：启动后在她身边招一只苦力怕，验证"不打开游戏屏"的彩蛋路径</summary>
        public static bool CreeperTest;
        /// <summary>开发用（--shutdowntest）：启动 4s 后从 gaming 切到 idle，验证"关游戏动画"</summary>
        public static bool ShutdownTest;
        /// <summary>开发用（--scenetest）：自动走场景切换；可带模式 basic/all/fast（见启动处）</summary>
        public static bool SceneTest;
        /// <summary>--scenetest 的模式：basic（默认）/ all（全配对矩阵）/ fast（0.8s 极速连切）</summary>
        public static string SceneTestMode = "basic";
        /// <summary>开发用（--clicktest）：启动 3s 后模拟一次"单击她"，复现点击相关链路</summary>
        public static bool ClickTest;
        /// <summary>开发用（--worktest）：启动后完整演一遍"被叫去干活"（打断 + 惊讶脸 + 暂停条 + 关屏 + 冥想）</summary>
        public static bool WorkTest;
        /// <summary>开发用（--uitest）：按用户操作序列跑一遍回归（调用同一批处理函数）</summary>
        public static bool UiTest;
        /// <summary>开发用（--menutest）：菜单树自检（关键项齐全、文字非空）</summary>
        public static bool MenuTest;
        /// <summary>开发用（--balancetest）：注入伪余额事件（扣费/低额/充值/失败）</summary>
        public static bool BalanceTest;
        /// <summary>开发用（--dev）：右键菜单里显示开发者选项（普通用户默认看不到）</summary>
        public static bool DevMenu;
        /// <summary>开发用（--autoscene idle|gaming|doze|off）：把桌宠直接置于某个自动状态，便于观察与测试切换</summary>
        public static string AutoSceneTest = "";
        /// <summary>开发用（--beam on|off|debug）：调试投影光柱；debug = 实心洋红，用来确认几何到底画在哪</summary>
        public static string BeamTest = "";

        [STAThread]
        public static void Main(string[] args)
        {
            bool created;
            Mutex mutex = new Mutex(true, "DeepSeekPet_SingleInstance", out created);
            if (!created) return;
            try
            {
                PetConfig cfg = PetConfig.Load();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--url" && i + 1 < args.Length) cfg.stateUrl = args[i + 1];
                    if (args[i] == "--debug") PetWindow.DebugMode = true;
                    if (args[i] == "--noanim") PetWindow.NoAnim = true;
                    if (args[i] == "--nopoll") PetWindow.NoPoll = true;
                    if (args[i] == "--notray") PetWindow.NoTray = true;
                    if (args[i] == "--nomenu") PetWindow.NoMenu = true;
                    if (args[i] == "--nosprite") PetWindow.NoSprite = true;
                    if (args[i] == "--opaque") PetWindow.OpaqueMode = true;
                    if (args[i] == "--screen" && i + 1 < args.Length) cfg.screenMode = args[i + 1];
                    if (args[i] == "--game" && i + 1 < args.Length) cfg.game = args[i + 1];
                    if (args[i] == "--vnmood" && i + 1 < args.Length) VnMood = args[i + 1];
                    if (args[i] == "--sidetest") SideTest = true;
                    if (args[i] == "--screenonly") ScreenOnlyTest = true;
                    if (args[i] == "--posetest" && i + 1 < args.Length) PoseTest = args[i + 1];
                    if (args[i] == "--creepertest") CreeperTest = true;   // 开发用：启动 2.5s 后在她身边招一只苦力怕（不碰游戏屏）
                    if (args[i] == "--shutdowntest") ShutdownTest = true; // 开发用：启动 4s 后 gaming → idle，验证关游戏动画
                    if (args[i] == "--scenetest") { SceneTest = true; if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) SceneTestMode = args[i + 1]; }
                    if (args[i] == "--autoscene" && i + 1 < args.Length) AutoSceneTest = args[i + 1];   // 开发用：直接置于某自动状态
                    if (args[i] == "--clicktest") ClickTest = true;                                      // 开发用：模拟单击她
                    if (args[i] == "--worktest") WorkTest = true;
                    if (args[i] == "--uitest") UiTest = true;
                    if (args[i] == "--menutest") MenuTest = true;
                    if (args[i] == "--balancetest") BalanceTest = true;                                        // 开发用：演一遍"被打断"完整链路
                    if (args[i] == "--dev") DevMenu = true;                                              // 显示开发者菜单项
                    if (args[i] == "--beam" && i + 1 < args.Length) BeamTest = args[i + 1];
                    if (args[i] == "--sidedeath") SideDeath = true;
                    if (args[i] == "--noparticles") cfg.particles = false;
                    if (args[i] == "--nosway") cfg.sway = false;
                    if (args[i] == "--mcdump" && i + 1 < args.Length) { PetWindow.DumpMcScreen(args[i + 1]); return; }
                    if (args[i] == "--pos" && i + 1 < args.Length)
                    {
                        string[] xy = args[i + 1].Split(',');
                        if (xy.Length == 2) { cfg.x = double.Parse(xy[0]); cfg.y = double.Parse(xy[1]); }
                    }
                }
                PetWindow win = new PetWindow(cfg);
                win.Run();
            }
            catch (Exception ex)
            {
                PetConfig.Log("fatal: " + ex.ToString());
                MessageBox.Show("桌宠启动失败：" + ex.Message + "\n详见 %APPDATA%\\deepseek-pet\\pet.log", "DeepSeek 桌宠");
            }
            finally { GC.KeepAlive(mutex); }
        }
    }

    class PetWindow
    {
        // --- 运行时状态 ---
        PetConfig cfg;
        Application app;
        Window win;
        Grid root, spriteStack;
        Canvas particleLayer;
        Image imgBase, imgFade, imgBlink;
        Border bubble, chip;
        TextBlock bubbleText, chipText, chipIcon;
        Ellipse chipDot;
        TranslateTransform tfTranslate;
        RotateTransform tfRotate;
        ScaleTransform tfScale;
        ContextMenu menu;
        MenuItem miBalance, miSway, miAuto, miTopmost;
        DispatcherTimer pollTimer, blinkTimer, idleTimer, blinkResetTimer;
        readonly List<Particle> parts = new List<Particle>();
        DateTime animStart = DateTime.Now;
        DateTime bounceUntil = DateTime.MinValue;
        Thread pollThread;
        volatile bool running = true;
        readonly object stateLock = new object();
        PetSnapshot latest;
        PetSnapshot shown = new PetSnapshot();
        Dictionary<string, BitmapImage> moods = new Dictionary<string, BitmapImage>();
        string currentMood = "idle";
        string assetsDir = "";
        DateTime lastUserAction = DateTime.Now;
        // 「场景计时」与「互动计时」分开（用户要求）：
        //   单击她 → 切回待机（重置场景计时）；
        //   拖动 / 右键菜单 → **保持她原来的状态**（只算互动，不重置场景计时）。
        DateTime lastSceneReset = DateTime.Now;
        // 被点醒后的"迷糊缓冲"：这段时间内她仍是打瞌睡姿态 + 困倦表情（约 2.5s），
        // 之后再自然回到待机 —— 避免出现"困倦脸与待机立绘中途互相插队"的割裂（用户报的 bug）
        DateTime dozeWakeUntil = DateTime.MinValue;
        DateTime devHoldUntil = DateTime.MinValue;   // 开发者"置于某状态"后的观察期：期内冻结场景机
        DateTime settleUntil = DateTime.MinValue;    // "叹气落座"过渡到期时刻（进入冥想前的 400ms 下沉）
        int nextFadeMs = 0;                          // 下一次交叉淡入的时长覆盖（0=用默认 320ms）
        bool fadingThrough = false;
        readonly System.Collections.Generic.Queue<object[]> sayQueue = new System.Collections.Generic.Queue<object[]>();   // 台词排队（避免多句互相覆盖）
        bool sayBusy = false;                  // 渐变过渡进行中：姿态不变量必须让路，否则会先跳一帧
        DateTime lastPoll = DateTime.MinValue;
        int failCount = 0;
        bool blinkBusy = false;
        string lastMessage = "";
        Random rnd = new Random();
        WinForms.NotifyIcon tray;
        DateTime suspendBlinkUntil = DateTime.MinValue;
        // ---- 姿态集（v0.2.0）：assets\poses\<state>.png —— 整张姿态图，丢进去即生效 ----
        //   idle 不用文件（走 v1 站立立绘 + 表情系统）；pick/doze/handheld/meditate 各一张，
        //   可选 <state>_t1.png 作为切换过渡帧（播 ~170ms 再落到正式姿态）。
        //   素材尺寸约定：与站立立绘同画布（720×1090），这样窗口与位置都不跳。
        static readonly string[] PoseNames = new string[] { "pick", "doze", "handheld", "meditate", "handheld_surprise" };
        readonly Dictionary<string, BitmapImage> poses = new Dictionary<string, BitmapImage>();
        readonly Dictionary<string, BitmapImage> poseTrans = new Dictionary<string, BitmapImage>();
        string pose = "idle";
        DateTime poseTransUntil = DateTime.MinValue;
        DateTime zzzNext = DateTime.MinValue;
        DateTime bubbleNext = DateTime.MinValue;    // 常驻气泡下一串的时间      // 打瞌睡时下一颗 Zzz 的时间
        bool screenOnlyApplied;
        System.Windows.Shapes.Polygon beam;        // 身前极淡雾层
        System.Windows.Shapes.Polygon beamBack;    // 身后亮层（光柱主体）
        Ellipse beamGlow;                          // 掌机处的辉光
        System.Windows.Shapes.Rectangle beamEdge;  // 掌机上边缘的溢光边
        LinearGradientBrush beamBrush, beamBrushSoft;
        LinearGradientBrush beamMask;              // 水平羽化遮罩：左右边缘化开，消除硬斜边
        // （原光柱微尘的计时器字段 moteNext 已随微尘功能删除）
        bool bootPending;                          // 正在播"举机"过渡帧：设备还没开机（不出光、不出屏）
        bool dragging;                             // 正在被拖动（拖着的时候不玩游戏、不出光）
        bool pickShown;                            // 本次按下是否已进入"被拎起"（长按 180ms 才算）
        DispatcherTimer pickTimer;                 // 长按判定：单击=互动，按住=拎起
        bool skipTrans;                            // 本次切姿态跳过过渡帧（拖拽恢复时用，用户不想要"举机"闪一下）
        DateTime dragIdleUntil = DateTime.MinValue;// 预留：松手后强制待机到某时刻（当前不用）
        DateTime moodHoldUntil = DateTime.MinValue;// 点击/充值等"即时表情"的保持期，期间不许被 ApplyMood 刷回
        string manualScene = null;                 // 菜单手动指定姿态时锁定的场景（null=跟随自动规则）
        string devAutoScene = null;                // 开发用：强制"自动状态"走待机/游戏/打瞌睡（null=按计时）
        int saySeq = 0;                            // 台词序号：防止被替换掉的旧淡出动画把新气泡隐藏
        double lastAnnouncedTotal = double.NaN;    // 已播报过的余额数值（去重）
        DateTime balanceAnnounceUntil = DateTime.MinValue;  // 余额播报冷却到期时刻
        // 光柱锚点（设备矩形，按立绘画布比例）：来自 assets\poses\<姿态>.json，缺省用经验值
        double beamAnchorTop = 0.60, beamAnchorW = 0.24, beamAnchorCx = 0.5;

        const double BubbleRow = 88;   // 气泡固定占位高度：避免说话时立绘上下跳动
        const double ScreenW = 240;    // 游戏屏显示尺寸（120×80 游戏像素 ×2，最近邻放大）
        const double ScreenH = 160;
        const double ScreenChrome = 20;// 游戏屏出现时额外占用的高度（边框 + 间距）
        public static bool DebugMode = false;
        public static bool NoAnim, NoPoll, NoTray, NoMenu, NoSprite;
        public static bool OpaqueMode = false;

        // ---- 游戏屏与场景 ----
        IGameScreen mc;
        Border screenBezel;
        // 注：书桌场景模式（假背景 / 前景桌面 / 代码画的桌沿键鼠条）与一体化补丁帧
        //（键鼠按下帧、表情差分帧、开发验收开关）已在本次清理中整体移除，含其全部字段与素材加载。
        Border screenCharHost;
        TextBlock screenText, screenSubText, screenDialog;   // 注：原木标签已删（数量改由 F3 的 XYZ 显示）
        Border screenFlash, screenLine, screenPause;      // 「被叫去干活」统一演出的叠加层
        TextBlock screenPauseText;
        ScaleTransform screenScale;
        TranslateTransform screenShake;
        int workPhase;                                     // 0=无 1=各屏惊一下 2=关屏动画
        double workT;
        Image screenBg, screenImage, screenCharImg;
        Border[] screenChoiceBoxes;
        TextBlock[] screenChoiceLabels;
        readonly Dictionary<string, BitmapImage> vnArtCache = new Dictionary<string, BitmapImage>();
        DispatcherTimer clickTimer;
        int pendingClicks;
        DispatcherTimer animTimer;
        bool screenVisible;
        string scene = "idle";         // idle | gaming | doze | work
        long lastEventSeq = -1;
        DateTime busyUntil = DateTime.MinValue;

        static readonly string[] Lines = new string[] {
            "主人，今天也要加油呀！",
            "深海里的鲸鱼都在给你打气～",
            "记得多喝水，别一直盯着屏幕哦",
            "余额还够花，放心用吧！",
            "又有新任务了吗？我准备好啦",
            "任务做完了记得摸鱼休息一下",
            "我在这儿陪着你的说～"
        };

        /// <summary>游戏台词（MC 3D / 2D 横版共用 —— 两者都是"我的世界"语境）</summary>
        static readonly string[] GameLines = new string[] {
            "天快黑了，得赶紧搭个房子…",
            "别打扰我，我在砍树呢！",
            "这个矿洞有点深…",
            "再挖一块钻石就收工…",
            "嘘——前面有苦力怕",
            "等我搭完这个房子就陪你～",
            "裙摆老是穿模…不过跑图挺快"
        };

        /// <summary>galgame 台词（不能说"砍树"—— 语境是剧情与角色交流）</summary>
        static readonly string[] VnLines = new string[] {
            "这个角色的好感度…有点难刷呢",
            "别吵，我在看剧情！",
            "唔…这个选项选哪个好呢…",
            "刚刚那句话是不是有点心动…",
            "这条线我还没通呢～",
            "下次要不要换个角色追追看？"
        };

        /// <summary>深度求索（冥想/干活）时的点击台词 —— 用户反馈：此前走的是日常台词，出戏</summary>
        static readonly string[] WorkLines = new string[] {
            "嘘…我在想问题呢",
            "这个问题有点绕…",
            "唔…让我再想想",
            "正在深度求索，稍等一下～",
            "这个难点终于想通了！",
            "主人先忙，我这边快好了～"
        };

        /// <summary>进入游戏时的台词：按当前游戏画面选（MC 3D/2D 共用一套，galgame 另一套）
        /// 主台词用用户指定的玩梗："爱慕西，启动！" / "旮旯给木，启动！"，另各配两条备选避免复读</summary>
        string GamingEntryLine()
        {
            string[] mc = new string[] { "爱慕西，启动！", "我的世界，启动！", "挖矿时间到！" };
            string[] vn = new string[] { "旮旯给木，启动！", "开始攻略！" };
            string[] src = cfg.game == "vn" ? vn : mc;
            return src[rnd.Next(src.Length)];
        }

        /// <summary>点游戏屏换游戏时的台词（用户要求：切换游戏也要有配套台词），按**换到的那款**选</summary>
        string GameSwitchLine()
        {
            string[] mc = new string[] { "重新开个存档～", "换个地图挖挖看", "这次玩点不一样的" };
            string[] vn = new string[] { "换条线推推～", "换个角色攻略！", "这条线好像也不错…" };
            string[] src = cfg.game == "vn" ? vn : mc;
            return src[rnd.Next(src.Length)];
        }

        /// <summary>玩游戏时点她的台词：同样按游戏画面选</summary>
        string GamingClickLine()
        {
            string[] src = cfg.game == "vn" ? VnLines : GameLines;
            return src[rnd.Next(src.Length)];
        }

        /// <summary>【锁定】打瞌睡时的点击台词＝梦话（用户要求：zzzz / 红烧肉）</summary>
        static readonly string[] DozeLines = new string[] {
            "zzzz……",
            "嘿嘿…红烧肉好吃……",
            "唔…再睡五分钟…就五分钟……",
            "……鲸鱼…飞起来了……",
            "zzz…（翻了个身）"
        };

        /// <summary>自动模式：睡着时被点醒 → 回待机，说迷糊的清醒台词（用户给的语感）</summary>
        static readonly string[] WakeLines = new string[] {
            "嗯…嗯？需要我做什么？",
            "有、有活要干了嘛！",
            "啊！我醒了我醒了！",
            "唔…我没睡，我在想事情…"
        };

        /// <summary>切换状态前把上一句台词清掉（用户要求：切模式时旧台词不该残留，"出戏"）</summary>
        void HideBubble()
        {
            saySeq++;                                  // 让尚未触发的淡出回调失效
            sayQueue.Clear(); sayBusy = false;          // 丢弃排队中的旧台词（用户要求：切状态不残留）
            if (bubble == null) return;
            bubble.BeginAnimation(UIElement.OpacityProperty, null);
            bubble.Opacity = 0;
            bubble.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// 开发者：完整复现"被叫去干活"的演出 —— 先把她放进游戏状态（让演出条件成立），
        /// 2.5 秒后调用**真实链路** OnWorkStart()：惊一下 + 惊讶脸 + 暂停条 + 关屏动画 + 转冥想。
        /// </summary>
        void RunInterruptDemo()
        {
            HideBubble();
            devHoldUntil = DateTime.MinValue;      // 别让开发者观察期挡住后续场景流转
            if (scene != "gaming") ForceAutoState("gaming");
            else PetConfig.Log("dev: 打断演出 —— 她已在 gaming，直接等 2.5s 触发");
            PetConfig.Log("dev: 打断演出 —— 先置于 gaming，2.5s 后触发 OnWorkStart");
            DispatcherTimer dt = new DispatcherTimer();
            dt.Interval = TimeSpan.FromMilliseconds(2500);
            dt.Tick += delegate
            {
                dt.Stop();
                devHoldUntil = DateTime.MinValue;   // 解除冻结，让 work → idle 的正常流转能发生
                PetConfig.Log("dev: 打断演出 —— 调用真实链路 OnWorkStart()");
                OnWorkStart();
            };
            dt.Start();
        }

        /// <summary>菜单/开发者直接置于"打瞌睡"时的台词（此前该路径不经过 SetScene 的 doze 分支，所以没台词）</summary>
        static readonly string[] DozeEntryLines = new string[] {
            "那我先睡一会儿…",
            "睡觉去了～ zzz",
            "唔…好困，眯一下……",
            "晚安…（揉眼睛）"
        };

        /// <summary>被拎起来时的台词（拖动拎起 / 菜单锁定"被拎起来"共用）</summary>
        static readonly string[] PickLines = new string[] {
            "你、你做什么！",
            "把我放下啦～",
            "主人，你在干什么？",
            "呀！别拎着我…",
            "唔…脚够不到地了…",
            "放我下来，我自己会走！"
        };

        /// <summary>一份红烧肉的价格（¥）——用于把余额换算成"还能买几份红烧肉"</summary>
        const double PorkPrice = 8.0;

        /// <summary>
        /// 开发者用：把她**直接放进**某个自动状态（不是"强制锁定"）——
        /// 做法是调整两个计时基准，让场景机按**正常规则**自然切过去（动画与台词都走真实链路），
        /// 之后她仍按规则继续演化，这样才方便测试"从这个状态切换到别的状态"。
        /// </summary>
        void ForceAutoState(string s)
        {
            DateTime now = DateTime.Now;
            HideBubble();                       // 切状态前清掉旧台词
            manualScene = null;                 // 清掉会干扰的手动锁定
            moodHoldUntil = DateTime.MinValue;  // 清掉表情保持期（否则"困倦脸"会赖着不走）
            dozeWakeUntil = DateTime.MinValue;
            if (s == "idle") { lastUserAction = now; lastSceneReset = now; }
            else if (s == "gaming") { lastUserAction = now; lastSceneReset = now.AddSeconds(-(cfg.gameIdleSeconds + 1)); }
            else if (s == "doze") { lastUserAction = now.AddSeconds(-(cfg.sleepIdleSeconds + 1)); lastSceneReset = now; }
            else if (s == "interrupted") { lastUserAction = now; lastSceneReset = now; busyUntil = now.AddMinutes(3); }   // 被打断：屏幕留着 + 惊讶脸
            // ⚠ 关键：常开模式（screenMode=on）的规则**优先于计时判断**，只改计时基准根本切不过去。
            // 所以要**直接 SetScene**（走真实链路：动画 + 台词），并短暂冻结场景机，让你能观察这个状态；
            // 冻结期结束后（或你点一下）自动规则立刻接管 —— 这正是"置于该状态以便测试切换"的语义。
            // 观察期设为"无限期"：常开模式下用户就是想稳定观察某个非游戏状态（否则 20s 后会被常开规则推回 gaming）。
            // 退出方式：菜单选「跟随计时（默认）」，或点她一下（单击＝恢复自动规则）。
            devHoldUntil = DateTime.MaxValue;
            // （置于游戏时不再自己说进场词：由 SetScene 统一说，避免同帧两句互相打断）
            SetScene(s);
            PetConfig.Log("dev: 置于自动状态 = " + s + "（直接切换 + 冻结场景机；点她一下或选\"跟随计时\"恢复自动）");
        }

        /// <summary>余额评论：按"能买几份红烧肉"分档（>2 份=还很够 / 1–2 份=不太够 / <1 份=要没钱了）</summary>
        string BalanceComment(double total)
        {
            string[] rich = new string[] { "还很多呢，放心用～", "够花一阵子啦", "主人真有钱～" };
            string[] mid = new string[] { "不太够了…省着点用哦", "就剩一份红烧肉的钱了", "有点紧巴巴的" };
            string[] poor = new string[] { "要没钱了…", "呜呜，快见底了", "得跟主人要零花钱了…" };
            string[] src = (total >= PorkPrice * 2) ? rich : ((total >= PorkPrice) ? mid : poor);
            return src[rnd.Next(src.Length)];
        }

        /// <summary>红烧肉换算（**随机触发**，不是每次都说）</summary>
        string PorkLine(double total)
        {
            if (rnd.NextDouble() > 0.45) return "";
            return "（还能买 " + PorkCount(total) + " 份红烧肉）";
        }

        public PetWindow(PetConfig config) { cfg = config; }

        // ================= 启动 =================
        public void Run()
        {
            assetsDir = ResolveAssetsDir();
            app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            BuildWindow();
            if (!NoSprite) LoadSprites();
            if (!NoPoll) StartPolling();
            if (!NoAnim) StartAnimation();
            win.Show();
            PetConfig.Log(string.Format(CultureInfo.InvariantCulture,
                "DeepSeekPet v{0} 启动：assets={1} stateUrl={2} size={3}x{4} spawn={5}",
                PetApp.Version, assetsDir, cfg.stateUrl, win.Width, win.Height, NoSprite ? "minimal" : "normal"));
            if (!NoSprite) Say("我是 DeepSeek～下面能看见余额哦", 7);   // 余额徽章在她正下方（用户指正：不是右下角）   // 用户要求：问候语不带"桌宠 vX.Y.Z"（软件+版本号太出戏）
            // 开发用：把游戏屏在物理屏幕上的矩形写进日志（截图与自动化点击要按真实坐标来）
            DispatcherTimer posLog = new DispatcherTimer();
            posLog.Interval = TimeSpan.FromMilliseconds(1200);
            posLog.Tick += delegate
            {
                posLog.Stop();
                try
                {
                    if (screenImage != null && screenImage.ActualWidth > 0)
                    {
                        Point a = screenImage.PointToScreen(new Point(0, 0));
                        Point b = screenImage.PointToScreen(new Point(screenImage.ActualWidth, screenImage.ActualHeight));
                        PetConfig.Log(string.Format(CultureInfo.InvariantCulture,
                            "screen rect physical {0:F0},{1:F0} -> {2:F0},{3:F0} (dip {4:F0}x{5:F0})",
                            a.X, a.Y, b.X, b.Y, screenImage.ActualWidth, screenImage.ActualHeight));
                    }
                }
                catch { }
                // 开发用：--sidedeath 十秒后强制死亡（验收"死亡 → 重新加载"）
                if (PetApp.SideDeath)
                {
                    DispatcherTimer dtKill = new DispatcherTimer();
                    dtKill.Interval = TimeSpan.FromSeconds(10);
                    dtKill.Tick += delegate { dtKill.Stop(); McSide s2 = mc as McSide; if (s2 != null) s2.DevKill(); };
                    dtKill.Start();
                }
                // 开发用：--vnmood love|angry 立刻演一次特殊画面（验收截图用）
                try
                {
                    McVn vn = mc as McVn;
                    if (vn != null && !string.IsNullOrEmpty(PetApp.VnMood)) vn.ForceMood(PetApp.VnMood);
                    McSide sd = mc as McSide;
                    if (sd != null && PetApp.SideTest) sd.DevSetup();
                }
                catch { }
            };
            posLog.Start();
            app.Run();
        }

        /// <summary>
        /// 开发用分镜导出：不开窗口，把游戏屏的两个段落各 16 帧拼成 PNG。
        /// 输出 &lt;path&gt;-chop.png（砍树循环）与 &lt;path&gt;-boom.png（苦力怕→You Died）。
        /// </summary>
        /// <summary>
        /// 点游戏屏：① 屏幕自己消费（galgame 选选项 / 跳过打字机）→ 到此为止；
        /// ② 双击 = 招一只苦力怕（彩蛋）；③ 单击 = 换下一个游戏画面。
        /// 单击延时 260ms 判定，避免和双击抢。
        /// </summary>
        void OnScreenClick(object sender, MouseButtonEventArgs e)
        {
            if (mc == null || screenImage == null) return;
            Point p = e.GetPosition(screenImage);
            double w = Math.Max(1, screenImage.ActualWidth), h = Math.Max(1, screenImage.ActualHeight);
            double nx = Math.Max(0, Math.Min(1, p.X / w)), ny = Math.Max(0, Math.Min(1, p.Y / h));
            bool consumed = mc.Click(nx, ny);
            PetConfig.Log(string.Format("screen click at {0:F0},{1:F0} of {2:F0}x{3:F0} -> nx={4:F3} ny={5:F3} consumed={6}",
                p.X, p.Y, w, h, nx, ny, consumed));
            if (consumed)
            {
                UpdateScreenOverlays();   // 立刻刷新（选项框要马上消失，不能等下一 tick）
                return;
            }
            if (clickTimer == null)
            {
                clickTimer = new DispatcherTimer();
                clickTimer.Interval = TimeSpan.FromMilliseconds(400);   // 双击判定窗口：260ms 太紧，慢一点的双击会被当成两次单击
                clickTimer.Tick += delegate
                {
                    clickTimer.Stop();
                    int n = pendingClicks;
                    pendingClicks = 0;
                    if (mc == null) return;
                    if (n >= 2) mc.DoubleClick();
                    else CycleGame();
                };
            }
            pendingClicks++;
            clickTimer.Stop();
            clickTimer.Start();
        }

        /// <summary>按配置创建游戏屏（所有屏都实现 IGameScreen，桌宠只认接口）</summary>
        static IGameScreen CreateScreen(string kind)
        {
            if (kind == "side") return new McSide();
            if (kind == "vn") return new McVn();
            return new McScreen();
        }

        static readonly string[] GameOrder = new string[] { "fp", "side", "vn" };

        /// <summary>换下一个游戏画面（点显示器 / 右键菜单）—— 这属于"用户手动选定"，之后不再随机</summary>
        void CycleGame()
        {
            int i = Array.IndexOf(GameOrder, cfg.game);
            cfg.game = GameOrder[(i + 1 + GameOrder.Length) % GameOrder.Length];
            cfg.gameFixed = true;                      // 手动切过 → 固定住（用户要求）
            cfg.Save();
            SwitchScreen();
            PetConfig.Log("gaming: 手动切换到 " + cfg.game + "（已固定）");
            // （换游戏台词已与"换个游戏：X"合并，避免同帧两句其中一句被丢弃）
        }

        /// <summary>切换游戏画面：重建屏幕对象并换掉 Image 的 Source</summary>
        void SwitchScreen()
        {
            IGameScreen next = CreateScreen(cfg.game);
            next.CreeperChance = cfg.creeperChance;
            mc = next;
            if (screenImage != null) screenImage.Source = mc.Source;
            mc.Reset();
            UpdateScreenOverlays();
            cfg.Save();
            SayKey(GameSwitchLine(), 3);   // 换游戏：用顺口的那套（用户反馈"换个游戏：第一人称砍树"太生硬）
        }

        /// <summary>
        /// 加载 assets/vn 下的素材：先找 local/&lt;id&gt;.png|.jpg（本机自用档），再找 &lt;id&gt;.png|.jpg（随插件分发的原创素材）。
        /// 结果进缓存（null 也缓存），避免每帧查盘。
        /// </summary>
        BitmapImage LoadVnAsset(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (vnArtCache.ContainsKey(id)) return vnArtCache[id];
            BitmapImage found = null;
            string[] exts = new string[] { ".png", ".jpg" };
            foreach (string sub in new string[] { "local", "" })
            {
                foreach (string ext in exts)
                {
                    string p = sub.Length == 0
                        ? Path.Combine(assetsDir, "vn", id + ext)
                        : Path.Combine(assetsDir, "vn", sub, id + ext);
                    if (!File.Exists(p)) continue;
                    try
                    {
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                        found = bi;
                        PetConfig.Log("vn asset: " + p);
                        break;
                    }
                    catch (Exception ex) { PetConfig.Log("vn asset failed: " + p + " " + ex.Message); }
                }
                if (found != null) break;
            }
            vnArtCache[id] = found;
            return found;
        }

        /// <summary>刷新 galgame 的立绘与背景</summary>
        void UpdateScreenCharacter()
        {
            if (mc == null) return;
            // 特殊演出（满好感/生气）优先用专属立绘，没有就退回普通立绘
            BitmapImage art = LoadVnAsset(mc.MoodAsset);
            if (art == null) art = LoadVnAsset(mc.CharacterAsset);
            BitmapImage bg = LoadVnAsset(mc.BackgroundAsset);
            McVn vn = mc as McVn;
            if (vn != null)
            {
                vn.HideBuiltinCharacter = (art != null);
                vn.HideBuiltinBackground = (bg != null);
            }
            if (screenCharHost != null)
            {
                screenCharImg.Source = art;
                screenCharHost.Visibility = art != null ? Visibility.Visible : Visibility.Collapsed;
            }
            if (screenBg != null)
            {
                screenBg.Source = bg;
                screenBg.Visibility = bg != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>刷新窗口层叠加：galgame 的中文台词与选项</summary>
        void UpdateScreenOverlays()
        {
            if (screenImage == null) return;
            UpdateScreenCharacter();
            string dlg = mc != null ? mc.OverlayText : "";
            string[] chs = (mc != null && mc.OverlayChoices != null) ? mc.OverlayChoices : new string[0];
            bool vn = mc is McVn;
            if (screenDialog != null)
            {
                screenDialog.Text = dlg != null ? dlg : "";
                // 台词长 → 自动缩一档字号/行距：否则两行时会被对话框下沿切掉（用户反馈的遮挡）
                int dn = screenDialog.Text.Length;
                if (dn > 40) { screenDialog.FontSize = 10; screenDialog.LineHeight = 13; }
                else if (dn > 26) { screenDialog.FontSize = 10.5; screenDialog.LineHeight = 14; }
                else { screenDialog.FontSize = 11.5; screenDialog.LineHeight = 15; }
                screenDialog.Visibility = (vn && !string.IsNullOrEmpty(dlg)) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (screenChoiceBoxes != null)
            {
                for (int i = 0; i < screenChoiceBoxes.Length; i++)
                {
                    bool show = vn && i < chs.Length;
                    screenChoiceBoxes[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (show) screenChoiceLabels[i].Text = chs[i];
                }
            }
        }

        public static void DumpMcScreen(string path)
        {
            // 输出名带版本号：证据图路径**绝不复用**。
            // （我连着三轮往 sheet-peek-3x.png 里写不同内容，DSH GUI 按路径缓存，用户一直看到第一版 —— 从结构上堵死这个坑。）
            path = path + "-v" + PetApp.Version;
            // 1) 砍树循环：每帧 0.15s，覆盖一次完整挥砍
            McScreen a = new McScreen();
            System.Collections.Generic.List<int[]> f1 = new System.Collections.Generic.List<int[]>();
            for (int i = 0; i < 16; i++) { for (int k = 0; k < 3; k++) a.Tick(0.05); f1.Add(a.Snapshot()); }
            McScreen.SaveSheet(f1, 4, path + "-chop.png");

            // 2) 苦力怕探头序列：连做 4 次（每次都等探头+冷却走完），正好能看出**位置与高度是随机的**
            McScreen b = new McScreen();
            for (int i = 0; i < 6; i++) b.Tick(0.05);
            System.Collections.Generic.List<int[]> f2 = new System.Collections.Generic.List<int[]>();
            for (int n = 0; n < 4; n++)
            {
                b.TriggerCreeper();
                for (int k = 0; k < 4; k++) { for (int j = 0; j < 5; j++) b.Tick(0.05); f2.Add(b.Snapshot()); }  // 每帧 0.25s
                for (int k = 0; k < 100; k++) b.Tick(0.05);     // 5.0s：探头 3.2s + 冷却 1.0s，走完才允许下一次
            }
            McScreen.SaveSheet(f2, 4, path + "-peek.png");

            // 3) 2D 横版：走 16 秒（含刷怪、砍树、可能被炸），每帧 1.0s
            McSide s = new McSide();
            System.Collections.Generic.List<int[]> f3 = new System.Collections.Generic.List<int[]>();
            for (int i = 0; i < 16; i++) { for (int k = 0; k < 20; k++) s.Tick(0.05); f3.Add(s.Snapshot()); }
            McScreen.SaveSheet(f3, 4, path + "-side.png");
            PetConfig.Log("mcdump -> " + path + "-chop/-boom/-side.png");
        }

        string ResolveAssetsDir()
        {
            if (!string.IsNullOrEmpty(cfg.assetsDir) && Directory.Exists(cfg.assetsDir)) return cfg.assetsDir;
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string[] candidates = new string[] {
                Path.Combine(exeDir, "assets"),
                Path.Combine(exeDir, "..", "assets"),
                Path.Combine(exeDir, "..", "..", "assets"),
            };
            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(Path.Combine(full, "idle.png"))) return full;
            }
            return Path.Combine(exeDir, "assets");
        }

        // ================= 界面 =================
        void BuildWindow()
        {
            win = new Window();
            win.WindowStyle = WindowStyle.None;
            win.AllowsTransparency = !OpaqueMode;
            win.Background = OpaqueMode ? Brushes.Red : Brushes.Transparent;
            win.Topmost = cfg.topmost;
            win.ShowInTaskbar = false;
            win.ShowActivated = false;
            win.ResizeMode = ResizeMode.NoResize;
            win.SnapsToDevicePixels = true;
            win.Title = "DeepSeek 桌宠 v" + PetApp.Version;
            win.Opacity = Math.Max(0.25, Math.Min(1.0, cfg.opacity));

            root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            ((RowDefinition)root.RowDefinitions[0]).Height = new GridLength(1, GridUnitType.Auto);   // 游戏屏
            ((RowDefinition)root.RowDefinitions[1]).Height = new GridLength(BubbleRow, GridUnitType.Pixel);
            ((RowDefinition)root.RowDefinitions[3]).Height = new GridLength(1, GridUnitType.Auto);

            // --- 游戏屏（纯代码像素画的小显示器，默认折叠；点它能立刻招来一只苦力怕）---
            mc = CreateScreen(cfg.game);
            mc.CreeperChance = cfg.creeperChance;
            screenImage = new Image();
            screenImage.Source = mc.Source;
            screenImage.Width = ScreenW;
            screenImage.Height = ScreenH;
            screenImage.Stretch = Stretch.Fill;
            RenderOptions.SetBitmapScalingMode(screenImage, BitmapScalingMode.NearestNeighbor);
            screenText = new TextBlock();
            screenText.Text = "You Died!";
            screenText.FontFamily = new FontFamily("Consolas, Microsoft YaHei UI, monospace");
            screenText.FontSize = 24;
            screenText.FontWeight = FontWeights.Bold;
            screenText.Foreground = Brushes.White;
            screenText.HorizontalAlignment = HorizontalAlignment.Center;
            screenText.VerticalAlignment = VerticalAlignment.Center;
            screenText.Visibility = Visibility.Collapsed;
            screenText.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 1, Color = Colors.Black };
            screenSubText = new TextBlock();
            screenSubText.Text = "Respawn";
            screenSubText.FontFamily = new FontFamily("Consolas, Microsoft YaHei UI, monospace");
            screenSubText.FontSize = 12;
            screenSubText.Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            screenSubText.HorizontalAlignment = HorizontalAlignment.Center;
            screenSubText.VerticalAlignment = VerticalAlignment.Bottom;
            screenSubText.Margin = new Thickness(0, 0, 0, 8);
            screenSubText.Visibility = Visibility.Collapsed;
            Grid screenGrid = new Grid();
            // 背景图层放在最底下：像素屏里 alpha=0 的地方就会透出这张背景（galgame 用的就是这条路）
            screenBg = new Image();
            screenBg.Stretch = Stretch.Fill;
            screenBg.Width = ScreenW;
            screenBg.Height = ScreenH;
            screenBg.IsHitTestVisible = false;
            screenBg.Visibility = Visibility.Collapsed;
            RenderOptions.SetBitmapScalingMode(screenBg, BitmapScalingMode.HighQuality);
            screenGrid.Children.Add(screenBg);
            screenGrid.Children.Add(screenImage);

            // --- galgame 屏的窗口层叠加：立绘 / 中文台词 / 选项（像素字体只有 ASCII，中文交给 WPF）---
            screenCharImg = new Image();
            screenCharImg.Stretch = Stretch.Uniform;
            screenCharImg.HorizontalAlignment = HorizontalAlignment.Center;
            screenCharImg.VerticalAlignment = VerticalAlignment.Top;
            screenCharImg.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(screenCharImg, BitmapScalingMode.HighQuality);
            Border charClip = new Border();
            charClip.Width = 172;
            charClip.Height = 118;
            charClip.HorizontalAlignment = HorizontalAlignment.Right;
            charClip.VerticalAlignment = VerticalAlignment.Top;
            charClip.Margin = new Thickness(0, 0, 2, 0);
            charClip.ClipToBounds = true;
            charClip.IsHitTestVisible = false;
            charClip.Child = screenCharImg;
            charClip.Visibility = Visibility.Collapsed;
            screenCharHost = charClip;
            screenGrid.Children.Add(charClip);

            screenDialog = new TextBlock();
            screenDialog.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
            screenDialog.FontSize = 11.5;
            screenDialog.LineHeight = 16;
            screenDialog.TextWrapping = TextWrapping.Wrap;
            screenDialog.Foreground = new SolidColorBrush(Color.FromArgb(255, 244, 244, 248));
            screenDialog.HorizontalAlignment = HorizontalAlignment.Left;
            screenDialog.VerticalAlignment = VerticalAlignment.Top;
            screenDialog.Margin = new Thickness(16, 118, 0, 0);
            screenDialog.Width = 208;
            screenDialog.IsHitTestVisible = false;
            screenDialog.Visibility = Visibility.Collapsed;
            screenGrid.Children.Add(screenDialog);

            screenChoiceBoxes = new Border[2];
            screenChoiceLabels = new TextBlock[2];
            for (int i = 0; i < 2; i++)
            {
                TextBlock lb = new TextBlock();
                lb.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
                lb.FontSize = 11.5;
                lb.Foreground = new SolidColorBrush(Color.FromArgb(255, 250, 240, 230));
                lb.VerticalAlignment = VerticalAlignment.Center;
                lb.Margin = new Thickness(12, 0, 0, 0);
                lb.IsHitTestVisible = false;
                Border bx = new Border();
                bx.Width = 184;
                bx.Height = 24;
                bx.HorizontalAlignment = HorizontalAlignment.Left;
                bx.VerticalAlignment = VerticalAlignment.Top;
                bx.Margin = new Thickness(28, 40 + i * 32, 0, 0);   // 与像素框同步上移（原来 52 会被下方名牌压住）
                bx.IsHitTestVisible = false;      // 点击统一由屏幕边框处理，交给 mc.Click 判定
                bx.Child = lb;
                bx.Visibility = Visibility.Collapsed;
                screenChoiceBoxes[i] = bx;
                screenChoiceLabels[i] = lb;
                screenGrid.Children.Add(bx);
            }

            screenGrid.Children.Add(screenSubText);
            screenGrid.Children.Add(screenText);

            // --- 「被叫去干活」统一演出的叠加层：暂停条 / 白闪 / 收屏亮线 ---
            screenPause = new Border();
            screenPause.Background = new SolidColorBrush(Color.FromArgb(235, 10, 12, 18));
            screenPause.Height = 30;
            screenPause.VerticalAlignment = VerticalAlignment.Top;
            screenPause.Margin = new Thickness(0, -30, 0, 0);
            screenPause.Opacity = 0;
            screenPause.IsHitTestVisible = false;
            screenPauseText = new TextBlock();
            screenPauseText.Text = "PAUSE";
            screenPauseText.FontFamily = new FontFamily("Consolas, Segoe UI, sans-serif");
            screenPauseText.FontSize = 13;
            screenPauseText.FontWeight = FontWeights.Bold;
            screenPauseText.Foreground = Brushes.White;
            screenPauseText.HorizontalAlignment = HorizontalAlignment.Center;
            screenPauseText.VerticalAlignment = VerticalAlignment.Center;
            screenPause.Child = screenPauseText;
            screenGrid.Children.Add(screenPause);

            screenFlash = new Border();
            screenFlash.Background = Brushes.White;
            screenFlash.Opacity = 0;
            screenFlash.IsHitTestVisible = false;
            screenGrid.Children.Add(screenFlash);

            screenLine = new Border();
            screenLine.Background = Brushes.White;
            screenLine.Height = 2;
            screenLine.VerticalAlignment = VerticalAlignment.Center;
            screenLine.Opacity = 0;
            screenLine.IsHitTestVisible = false;
            screenGrid.Children.Add(screenLine);

            screenBezel = new Border();
            screenBezel.CornerRadius = new CornerRadius(10);
            screenBezel.Background = new SolidColorBrush(Color.FromArgb(255, 20, 22, 28));
            screenBezel.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 74, 82, 102));
            screenBezel.BorderThickness = new Thickness(6);
            screenBezel.HorizontalAlignment = HorizontalAlignment.Center;
            screenBezel.VerticalAlignment = VerticalAlignment.Bottom;
            screenBezel.Margin = new Thickness(4, 0, 4, 2);
            screenBezel.Visibility = Visibility.Hidden;   // 用 Hidden 保留行高（避免气泡/立绘位置跳变）
            screenBezel.Cursor = Cursors.Hand;
            screenBezel.ToolTip = "单击换游戏 · 双击场景区：MC 招苦力怕 / galgame 切下一位角色 · galgame 可直接点选项";
            screenBezel.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black };
            screenBezel.Child = screenGrid;
            // 收屏动画用的变换（RenderTransform 不参与布局，所以不会引起窗口尺寸跳动）
            screenScale = new ScaleTransform(1, 1);
            screenShake = new TranslateTransform(0, 0);
            TransformGroup bezelTg = new TransformGroup();
            bezelTg.Children.Add(screenScale);
            bezelTg.Children.Add(screenShake);
            screenBezel.RenderTransform = bezelTg;
            screenBezel.RenderTransformOrigin = new Point(0.5, 0.5);
            screenBezel.MouseLeftButtonUp += OnScreenClick;
            // 关键：窗口级 OnMouseDown 会调 DragMove()，那是个模态拖动循环，会把随后的 MouseLeftButtonUp 一起吞掉，
            // 导致游戏屏上的点击（换游戏 / 招苦力怕 / galgame 选选项）永远收不到事件。
            // 在屏幕边框上把按下事件标记为已处理，窗口那条拖动逻辑就不会介入。
            screenBezel.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            Grid.SetRow(screenBezel, 0);

            root.Children.Add(screenBezel);

            // 游戏屏动画时钟（20 FPS，只在屏幕可见时干活）
            animTimer = new DispatcherTimer();
            animTimer.Interval = TimeSpan.FromMilliseconds(50);
            animTimer.Tick += delegate
            {
                StepWork(0.05);          // 「被叫去干活」的收尾动画要在屏幕隐藏后也能跑完
                UpdateBeamAndPose();     // 投影光柱 + 姿态优先不变量
                if (!screenVisible || mc == null) return;
                mc.Tick(0.05);
                screenText.Visibility = mc.ShowDiedOverlay ? Visibility.Visible : Visibility.Collapsed;
                screenSubText.Visibility = mc.ShowDiedOverlay ? Visibility.Visible : Visibility.Collapsed;
                // MC 的「原木 N」只属于两块 Minecraft 屏，galgame 屏不该出现
                UpdateScreenOverlays();
            };
            animTimer.Start();

            // --- 气泡 ---
            bubble = new Border();
            bubble.CornerRadius = new CornerRadius(14);
            bubble.Background = new SolidColorBrush(Color.FromArgb(242, 252, 253, 255));
            bubble.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 58, 106, 214));
            bubble.BorderThickness = new Thickness(1.6);
            bubble.Padding = new Thickness(12, 8, 12, 8);
            bubble.Margin = new Thickness(6, 4, 6, 2);
            bubble.VerticalAlignment = VerticalAlignment.Bottom;
            bubble.HorizontalAlignment = HorizontalAlignment.Center;
            bubble.MaxWidth = 320;
            bubble.Visibility = Visibility.Hidden;
            bubble.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.25, Color = Colors.Black };
            bubbleText = new TextBlock();
            bubbleText.TextWrapping = TextWrapping.Wrap;
            bubbleText.FontSize = 13.5;
            bubbleText.LineHeight = 20;
            bubbleText.Foreground = new SolidColorBrush(Color.FromArgb(255, 24, 42, 92));
            bubbleText.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
            TextOptions.SetTextFormattingMode(bubbleText, TextFormattingMode.Display);
            bubble.Child = bubbleText;
            Grid.SetRow(bubble, 1);
            root.Children.Add(bubble);

            // --- 立绘 ---
            // --- 掌机 → 游戏屏的投影光柱（纯代码特效；只在"她拿掌机 + 游戏屏可见"时出现）---
            beam = new System.Windows.Shapes.Polygon();
            beam.Points = new PointCollection();
            beamBrush = new LinearGradientBrush();
            // ⚠ 必须用绝对坐标映射：Stretch=None 时"相对包围盒"的渐变会退化 → 整个填充算成全透明
            //（上一版日志说 beam ON、画面上却什么都没有，就是这个）
            beamBrush.MappingMode = BrushMappingMode.Absolute;
            beamBrush.StartPoint = new Point(0, 500);    // 每次 UpdateBeam 会按实际几何重设
            beamBrush.EndPoint = new Point(0, 170);
            beamBrush.GradientStops.Add(new GradientStop(Color.FromArgb(6, 242, 250, 255), 0));
            beamBrush.GradientStops.Add(new GradientStop(Color.FromArgb(52, 222, 240, 255), 0.4));
            beamBrush.GradientStops.Add(new GradientStop(Color.FromArgb(88, 234, 246, 255), 0.78));
            beamBrush.GradientStops.Add(new GradientStop(Color.FromArgb(22, 226, 240, 255), 1));   // 顶端也淡出：双端无硬边
            beam.Fill = beamBrush;
            // ⚠ 关键：Polygon 默认 Stretch 会把几何拉伸铺满整个布局框 —— 那样锥形会被摊成
            // 一层满窗的雾（上一版"看不到光"的真因）。必须显式 None，让点坐标按原值绘制。
            beam.Stretch = Stretch.None;
            // 元素本身铺满整个 Grid 单元（不要 Left/Top！否则布局框只有几何那么大，
            // 超出的部分会被裁掉 —— 这是"日志说 ON、画面什么都没有"的第二个原因）
            beam.HorizontalAlignment = HorizontalAlignment.Stretch;
            beam.VerticalAlignment = VerticalAlignment.Stretch;
            beam.IsHitTestVisible = false;
            beam.Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(beam, 4);
            // 注意：这里先不 Add —— 光柱要在**顶层**（盖在她身上），见 spriteStack 添加之后

            // ---- 光效优化（v0.2.0）：身后亮层 + 掌机辉光；身前只留极淡雾（beam 本身）----
            beamBack = new System.Windows.Shapes.Polygon();
            beamBack.Stretch = Stretch.None;
            beamBack.HorizontalAlignment = HorizontalAlignment.Stretch;
            beamBack.VerticalAlignment = VerticalAlignment.Stretch;
            beamBack.IsHitTestVisible = false;
            beamBack.Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(beamBack, 4);
            beamBrushSoft = new LinearGradientBrush();
            beamBrushSoft.MappingMode = BrushMappingMode.Absolute;
            beamBrushSoft.GradientStops.Add(new GradientStop(Color.FromArgb(8, 228, 242, 255), 0));
            beamBrushSoft.GradientStops.Add(new GradientStop(Color.FromArgb(96, 206, 232, 255), 0.5));
            beamBrushSoft.GradientStops.Add(new GradientStop(Color.FromArgb(190, 232, 244, 255), 1));
            // 用户明确要求：光要在她**身前**看得见（两层里亮的那层不能在背后）
            // → 背后那层直接弃用（Opacity=0），只留身前这层。
            beamBack.Opacity = 0;
            root.Children.Add(beamBack);          // 加在立绘之前 = 光在她身后
            beamGlow = new Ellipse();             // 掌机处的辉光：让"光从设备发出"读得出来
            RadialGradientBrush glowBrush = new RadialGradientBrush();
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(200, 238, 248, 255), 0));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 192, 222, 255), 0.55));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 170, 210, 255), 1));
            beamGlow.Fill = glowBrush;
            beamGlow.Opacity = 0.45;   // 光源处只留一点点柔光，够"读得出从这儿来"就行
            beamGlow.IsHitTestVisible = false;
            beamGlow.Visibility = Visibility.Collapsed;
            beamGlow.HorizontalAlignment = HorizontalAlignment.Left;
            beamGlow.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRowSpan(beamGlow, 4);
            root.Children.Add(beamGlow);          // 也在立绘之前

            spriteStack = new Grid();
            spriteStack.RenderTransformOrigin = new Point(0.5, 1.0);
            TransformGroup tg = new TransformGroup();
            tfScale = new ScaleTransform(1, 1);
            tfRotate = new RotateTransform(0);
            tfTranslate = new TranslateTransform(0, 0);
            tg.Children.Add(tfScale);
            tg.Children.Add(tfRotate);
            tg.Children.Add(tfTranslate);
            spriteStack.RenderTransform = tg;

            imgBase = NewImage();
            imgFade = NewImage();
            imgFade.Opacity = 0;
            imgBlink = NewImage();
            imgBlink.Opacity = 0;
            spriteStack.Children.Add(imgBase);
            spriteStack.Children.Add(imgFade);
            spriteStack.Children.Add(imgBlink);
            Grid.SetRow(spriteStack, 2);
            root.Children.Add(spriteStack);
            root.Children.Add(beam);   // 光柱置于立绘之上：光要盖在她身上，而不是被后脑勺挡住
            // 掌机上边缘的"溢光边"：读成屏幕的光从机身缝里漏出来（用户要的"光从设备出来"）
            beamEdge = new System.Windows.Shapes.Rectangle();
            beamEdge.Fill = new SolidColorBrush(Color.FromArgb(165, 242, 250, 255));
            beamEdge.IsHitTestVisible = false;
            beamEdge.Visibility = Visibility.Collapsed;
            beamEdge.HorizontalAlignment = HorizontalAlignment.Left;
            beamEdge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRowSpan(beamEdge, 4);
            root.Children.Add(beamEdge);
            // 水平羽化遮罩（绝对坐标映射，几何变化时在 UpdateBeam 里重设）
            beamMask = new LinearGradientBrush();
            beamMask.MappingMode = BrushMappingMode.Absolute;
            beamMask.StartPoint = new Point(0, 0);
            beamMask.EndPoint = new Point(200, 0);
            beamMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            beamMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.2));
            beamMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.8));
            beamMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            beam.OpacityMask = beamMask;
            // 游戏屏提到光柱之上：光柱顶端绝不会压住屏幕内容（用户要求）
            if (screenBezel != null) { root.Children.Remove(screenBezel); root.Children.Add(screenBezel); }
            // 气泡提到最上层：光柱不许压住台词文字（用户反馈"影响看字"）
            if (bubble != null) { root.Children.Remove(bubble); root.Children.Add(bubble); }

            // --- 气泡粒子层（不挡鼠标）---
            particleLayer = new Canvas();
            particleLayer.IsHitTestVisible = false;
            Grid.SetRow(particleLayer, 2);
            root.Children.Add(particleLayer);

            // --- 余额徽章 ---
            chip = new Border();
            chip.CornerRadius = new CornerRadius(13);
            chip.Background = new SolidColorBrush(Color.FromArgb(235, 26, 40, 78));
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 92, 150, 235));
            chip.BorderThickness = new Thickness(1.2);
            chip.Padding = new Thickness(10, 3, 11, 4);
            chip.HorizontalAlignment = HorizontalAlignment.Center;
            chip.Margin = new Thickness(4, 4, 4, 6);
            chip.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black };
            StackPanel chipRow = new StackPanel();
            chipRow.Orientation = Orientation.Horizontal;
            chipDot = new Ellipse();
            chipDot.Width = 8; chipDot.Height = 8;
            chipDot.Margin = new Thickness(0, 0, 6, 0);
            chipDot.VerticalAlignment = VerticalAlignment.Center;
            chipDot.Fill = new SolidColorBrush(Color.FromArgb(255, 96, 220, 140));
            chipIcon = new TextBlock();
            chipIcon.Text = "";
            chipIcon.FontSize = 11.5;
            chipIcon.Margin = new Thickness(0, 0, 5, 0);
            chipIcon.VerticalAlignment = VerticalAlignment.Center;
            chipIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 176, 208, 255));
            chipText = new TextBlock();
            chipText.Text = "余额 --";
            chipText.FontSize = 13;
            chipText.VerticalAlignment = VerticalAlignment.Center;
            chipText.Foreground = Brushes.White;
            chipText.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
            TextOptions.SetTextFormattingMode(chipText, TextFormattingMode.Display);
            chipRow.Children.Add(chipDot);
            chipRow.Children.Add(chipIcon);
            chipRow.Children.Add(chipText);
            chip.Child = chipRow;
            Grid.SetRow(chip, 3);
            root.Children.Add(chip);
            chip.Visibility = cfg.showBalance ? Visibility.Visible : Visibility.Collapsed;

            win.Content = root;
            LoadPoses();                              // 姿态集（assets\poses\，缺素材自动回退）
            if (PetApp.ScreenOnlyTest) cfg.screenOnly = true;
            if (PetApp.AutoSceneTest.Length > 0 && PetApp.AutoSceneTest != "off" && PetApp.AutoSceneTest != "auto")
            {   // --autoscene：启动后直接把她放进该自动状态（改计时基准，走真实链路）
                string v = PetApp.AutoSceneTest;
                DispatcherTimer ast = new DispatcherTimer();
                ast.Interval = TimeSpan.FromMilliseconds(1200);
                ast.Tick += delegate { ast.Stop(); ForceAutoState(v); };
                ast.Start();
            }
            ApplyLayout();
            ApplyPosition();
            if (!NoMenu) BuildMenu();
            if (cfg.screenOnly) ApplyScreenOnly();
            if (PetApp.PoseTest.Length > 0) SetPose(PetApp.PoseTest, true);
            if (PetApp.MenuTest) CheckMenu();
            if (PetApp.BalanceTest) RunBalanceTest();   // 余额事件注入（--balancetest）   // 菜单自检（--menutest）
            if (PetApp.UiTest)        // 开发验收：按"用户操作"序列跑全量回归
            {
                DispatcherTimer ut = new DispatcherTimer();
                ut.Interval = TimeSpan.FromMilliseconds(2000);
                ut.Tick += delegate { ut.Stop(); RunUiTest(); };
                ut.Start();
            }            if (PetApp.WorkTest)      // 开发验收：启动 3s 后完整演一遍"被叫去干活"
            {
                DispatcherTimer wt = new DispatcherTimer();
                wt.Interval = TimeSpan.FromMilliseconds(3000);
                wt.Tick += delegate { wt.Stop(); RunInterruptDemo(); };
                wt.Start();
            }
            if (PetApp.ClickTest)     // 开发验收：3s 后模拟一次"单击她"，用于复现点击相关的链路
            {
                DispatcherTimer ctt = new DispatcherTimer();
                ctt.Interval = TimeSpan.FromMilliseconds(3000);
                ctt.Tick += delegate { ctt.Stop(); PetConfig.Log("clicktest: 模拟单击"); OnPetted(); };
                ctt.Start();
            }
            if (PetApp.CreeperTest)   // 开发验收：2.5s 后招一只苦力怕（应当**不出现**游戏屏）
            {
                DispatcherTimer ct = new DispatcherTimer();
                ct.Interval = TimeSpan.FromMilliseconds(2500);
                ct.Tick += delegate { ct.Stop(); SpawnPetCreeper(); };
                ct.Start();
            }
            if (PetApp.SceneTest)    // 开发验收：按顺序走完所有场景切换（每步 4 秒），核对台词与多余帧
            {
                int idx = 0;
                DispatcherTimer stt = new DispatcherTimer();
                string[] basic = new string[] { "idle", "gaming", "doze", "work", "interrupted", "gaming", "idle" };
                string[] matrix = new string[] {
                    "idle","gaming","idle",
                    "doze","gaming","doze",
                    "work","idle","work","gaming",
                    "interrupted","gaming","interrupted","doze",
                    "work","doze","gaming","idle"
                };
                string[] fastSeq = new string[] { "gaming","idle","doze","gaming","work","gaming","doze","idle","work","idle","gaming","doze" };
                bool all = (PetApp.SceneTestMode == "all");
                bool fast = (PetApp.SceneTestMode == "fast");
                string[] seq = all ? matrix : (fast ? fastSeq : basic);
                stt.Interval = TimeSpan.FromMilliseconds(fast ? 800 : (all ? 2200 : 4000));
                stt.Tick += delegate
                {
                    if (idx >= seq.Length) { stt.Stop(); PetConfig.Log("scenetest: done"); return; }
                    PetConfig.Log("scenetest: step " + idx + " -> " + seq[idx] + (all ? " [matrix]" : (fast ? " [fast]" : "")));
                    // 走与"菜单手动切状态"完全相同的路径（含 manualScene 锁定），否则 auto 模式下
                    // 场景机会立刻把手动值推回去，测出来的"多余帧"其实是测试装置的假象
                    if (seq[idx] == "interrupted") { scene = "interrupted"; PetConfig.Log("scene=interrupted (被叫去干活)"); }
                    // work 走**真实链路** OnWorkStart()（与插件工作事件完全同一条代码路径），
                    // 否则测不到"进入深度求索有没有台词"这类问题
                    else if (seq[idx] == "work") { PetConfig.Log("scenetest: 调用真实链路 OnWorkStart()"); manualScene = null; OnWorkStart(); }
                    else { manualScene = seq[idx]; SetScene(seq[idx]); }
                    idx++;
                };
                stt.Start();
            }
            if (PetApp.ShutdownTest)  // 开发验收：4s 后从 gaming 切 idle，验证关游戏动画
            {
                DispatcherTimer st = new DispatcherTimer();
                st.Interval = TimeSpan.FromMilliseconds(4000);
                st.Tick += delegate { st.Stop(); SetScene("idle"); };
                st.Start();
            }

            win.MouseLeftButtonDown += OnMouseDown;
            win.MouseRightButtonUp += OnRightUp;
            win.MouseWheel += OnWheel;
            win.MouseEnter += delegate
            {
                win.Cursor = Cursors.Hand;
                // ⚠ 悬停**不算把她叫醒**（用户规则：只有单击才切回待机）。
                // 以前这里无条件重置互动计时 → 睡着时鼠标一扫过就"跳过待机直接开玩"（用户报的现象）。
                // 现在：睡着时忽略悬停；醒着时才用它防止打瞌睡。
                if (scene != "doze" && pose != "doze") lastUserAction = DateTime.Now;
            };
            win.Closing += delegate { cfg.Save(); running = false; if (tray != null) { tray.Visible = false; tray.Dispose(); } };
            if (DebugMode)
            {
                root.Background = new SolidColorBrush(Color.FromArgb(110, 255, 0, 160));
                win.Loaded += delegate { LogDebug("Loaded"); };
                DispatcherTimer dbg = new DispatcherTimer();
                dbg.Interval = TimeSpan.FromSeconds(2.5);
                dbg.Tick += delegate { LogDebug("t+2.5s"); };
                dbg.Start();
            }
        }

        void LogDebug(string tag)
        {
            PetConfig.Log(string.Format(CultureInfo.InvariantCulture,
                "[debug {0}] win={1}x{2}@{3},{4} vis={5} opa={6} root={7}x{8} spriteStack={9}x{10} img={11}x{12} src={13} chip={14}x{15} row0={16} screen={17} {18}x{19} scene={20} screenVisible={21}",
                tag, win.ActualWidth, win.ActualHeight, win.Left, win.Top, win.IsVisible, win.Opacity,
                root.ActualWidth, root.ActualHeight, spriteStack.ActualWidth, spriteStack.ActualHeight,
                imgBase.ActualWidth, imgBase.ActualHeight, (imgBase.Source != null),
                chip.ActualWidth, chip.ActualHeight,
                ((RowDefinition)root.RowDefinitions[0]).ActualHeight,
                screenBezel != null ? screenBezel.Visibility.ToString() : "null",
                screenBezel != null ? screenBezel.ActualWidth : 0,
                screenBezel != null ? screenBezel.ActualHeight : 0,
                scene, screenVisible));
        }

        Image NewImage()
        {
            Image im = new Image();
            im.Stretch = Stretch.Uniform;
            im.HorizontalAlignment = HorizontalAlignment.Center;
            im.VerticalAlignment = VerticalAlignment.Bottom;
            im.SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            im.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Direction = 270, Opacity = 0.35, Color = Color.FromRgb(10, 20, 50) };
            return im;
        }

        void ApplyPosition()
        {
            Rect wa = SystemParameters.WorkArea;
            if (cfg.x < 0 || cfg.y < 0 || cfg.x > wa.Right - 40 || cfg.y > wa.Bottom - 40)
            {
                cfg.x = wa.Right - win.Width - 40;
                cfg.y = wa.Bottom - win.Height - 10;
            }
            win.Left = Math.Max(wa.Left, Math.Min(cfg.x, wa.Right - 40));
            win.Top = Math.Max(wa.Top, Math.Min(cfg.y, wa.Bottom - 40));
        }

        void BuildMenu()
        {
            menu = new ContextMenu();
            menu.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");

            // ================= 分类后的右键菜单（v0.2.1）=================
            // 一级只留 4 个分组 + 关于/退出，其余全部收进子菜单，避免一屏十几项找不到东西
            MenuItem grpBalance = new MenuItem(); grpBalance.Header = "余额";
            // grpHer 包装层已移除（用户要求扁平化）
            MenuItem grpGame = new MenuItem(); grpGame.Header = "游戏";
            MenuItem grpLook = new MenuItem(); grpLook.Header = "外观与位置";

            MenuItem miRefresh = new MenuItem(); miRefresh.Header = "立即刷新余额";
            miRefresh.Click += delegate { RefreshNow(true); };
            grpBalance.Items.Add(miRefresh);

            miBalance = new MenuItem(); miBalance.Header = "显示余额徽章"; miBalance.IsCheckable = true; miBalance.IsChecked = cfg.showBalance;
            miBalance.Click += delegate { cfg.showBalance = miBalance.IsChecked; chip.Visibility = cfg.showBalance ? Visibility.Visible : Visibility.Collapsed; cfg.Save(); };
            grpBalance.Items.Add(miBalance);
            menu.Items.Add(grpBalance);

            // ---- 让她做什么（状态切换）----
            MenuItem miState = new MenuItem(); miState.Header = "让她做什么（状态）";
            string[] sk = new string[] { "", "idle", "handheld", "doze", "meditate", "pick" };
            string[] sl = new string[] { "自动", "待机", "玩游戏", "打瞌睡", "冥想（深度求索）", "拎起来" };
            for (int i = 0; i < sk.Length; i++)
            {
                MenuItem mi = new MenuItem();
                mi.Header = sl[i];
                mi.IsCheckable = true;
                mi.Tag = sk[i];
                mi.IsChecked = (PetApp.PoseTest == sk[i]);
                if (sk[i] == "") miAuto = mi;      // ⚠ 只有状态菜单用 miAuto（表情菜单曾误用同一字段，已修）
                mi.Click += delegate(object s, RoutedEventArgs e)
                {
                    MenuItem m = (MenuItem)s;
                    string v = Convert.ToString(m.Tag);
                    PetApp.PoseTest = v;                       // 复用 PoseTest：非空时锁死该姿态
                    if (miAuto != null) miAuto.IsChecked = (v == "");
                    foreach (MenuItem other in ((MenuItem)m.Parent).Items)
                        if (other != m && other.IsCheckable) other.IsChecked = (Convert.ToString(other.Tag) == v);
                    if (v == "")
                    {
                        // 切回"自动"时把两个计时器**清零**（用户要求）：
                        // 否则之前开发者/手动切换伪造过的时间戳还在，一回到自动就立刻判成"该打游戏了"
                        lastUserAction = DateTime.Now;
                        lastSceneReset = DateTime.Now;
                        dozeWakeUntil = DateTime.MinValue;
                        devHoldUntil = DateTime.MinValue;
                        manualScene = null;
                        HideBubble();
                        Say("好的，我跟着情况来～", 3);
                        ApplyMood(true);
                        UpdateScene();                       // 立刻按新计时判定（应为待机）
                        SetPose(PoseForScene(), true);
                    }
                    else
                    {
                        SetPose(v, true);
                        // 用场景机自己的开关屏路径：它会同时处理收起、窗口高度与位置补偿
                        // （只改 screenBezel.Opacity 是不够的 —— 屏幕里的画面/ HUD 不都挂在那个外框下）
                        // 切状态前先清掉上一句台词：否则旧模式的台词会留在气泡里（出戏）
                        HideBubble();
                        // 手动切姿态时同时锁定场景：否则常开规则下一拍就把它推回 gaming，台词会串（用户报的 bug）
                        manualScene = (v == "handheld") ? "gaming" : "idle";
                        if (v == "handheld") { SetScene("gaming"); }
                        else { SetScene("idle"); }
                        // 手动锁定"被拎起来"时也说一句抱怨台词（与拖动拎起同一套）
                        if (v == "pick") SayKey(PickLines[rnd.Next(PickLines.Length)], 3.5);
                        // 手动置于"打瞌睡"：这条路径不经过 SetScene 的 doze 分支，所以在这里补台词
                        else if (v == "doze") SayKey(DozeEntryLines[rnd.Next(DozeEntryLines.Length)], 3.5);
                        // 手动置于"盘腿冥想（深度求索）"：同理补台词（此前这条路径不说话，
                        // 结果旧台词被清空、新状态又没接上话 = 用户报的"台词被挤掉"）
                        else if (v == "meditate") SayKey(rnd.Next(2) == 0 ? "进入深度求索…让我安静想想" : "正在深度求索，稍等一下～", 3.5);
                        // 不再说"好，我换个状态～"这种通用台词：各状态的台词由 SetScene 自己负责
                        // （gaming → "那我先砍会儿树"、doze → "唔……困了"、work → "进入深度求索…"）
                    }
                };
                miState.Items.Add(mi);
            }
            menu.Items.Add(miState);

            MenuItem miMood = new MenuItem(); miMood.Header = "表情";
            string[] keys = new string[] { "auto", "idle", "worry", "sleepy" };
            string[] labels = new string[] { "自动（跟随余额）", "开心", "担心", "困倦" };
            for (int i = 0; i < keys.Length; i++)
            {
                MenuItem mi = new MenuItem();
                mi.Header = labels[i];
                mi.IsCheckable = true;
                mi.Tag = keys[i];
                mi.IsChecked = (cfg.moodOverride == keys[i]);
                mi.Click += delegate(object s, RoutedEventArgs e)
                {
                    MenuItem m = (MenuItem)s;
                    cfg.moodOverride = Convert.ToString(m.Tag);
                    foreach (object o in miMood.Items) { MenuItem mm = o as MenuItem; if (mm != null) mm.IsChecked = (mm == m); }
                    cfg.Save();
                    ApplyMood(true);
                };
                miMood.Items.Add(mi);
            }
            menu.Items.Add(miMood);

            MenuItem miLook = new MenuItem(); miLook.Header = "动画";
            miSway = new MenuItem(); miSway.Header = "摇摆呼吸"; miSway.IsCheckable = true; miSway.IsChecked = cfg.sway;
            miSway.Click += delegate { cfg.sway = miSway.IsChecked; StartAnimation(); cfg.Save(); };
            // 「海底气泡」菜单项已删除（用户：脚底一圈泡泡观感不佳，整个功能废弃）
            // 「动画」子菜单已移除（只含一项，用户要求不单开一层）
            // 扁平化：三个子菜单已直接挂到第一层（不再套 grpHer）

            MenuItem miScreen = new MenuItem(); miScreen.Header = "游戏屏"; string[] smodes = new string[] { "auto", "on", "off" };
            string[] slabels = new string[] { "自动（久不互动就开玩）", "常开", "关闭" };
            for (int i = 0; i < smodes.Length; i++)
            {
                MenuItem mi = new MenuItem();
                mi.Header = slabels[i];
                mi.IsCheckable = true;
                mi.Tag = smodes[i];
                mi.IsChecked = (cfg.screenMode == smodes[i]);
                mi.Click += delegate(object s2, RoutedEventArgs e2)
                {
                    MenuItem m = (MenuItem)s2;
                    cfg.screenMode = Convert.ToString(m.Tag);
                    foreach (object o in miScreen.Items) { MenuItem mm = o as MenuItem; if (mm != null) mm.IsChecked = (mm == m); }
                    cfg.Save();
                    UpdateScene();
                };
                miScreen.Items.Add(mi);
            }
            grpGame.Items.Add(miScreen);

            // 「游戏画面」子菜单已移除（用户要求：点显示器即可换游戏；且此项原本只列了 fp/side，漏了 galgame）

            MenuItem miBoom = new MenuItem(); miBoom.Header = "招一只苦力怕（彩蛋）";
            miBoom.Click += delegate
            {
                // 屏幕开着 → 在游戏里招；屏幕没开 → 招到她身边
                // （**不再**用 SetScene("gaming") 强行开屏 —— 那会把她的待机/冥想状态打断，是 bug）
                if (screenVisible && mc != null) mc.TriggerCreeper();
                else SpawnPetCreeper();
            };
            grpGame.Items.Add(miBoom);

            MenuItem miScreenOnly = new MenuItem(); miScreenOnly.Header = "只看游戏屏（挂机模式）";
            miScreenOnly.IsCheckable = true; miScreenOnly.IsChecked = cfg.screenOnly;
            miScreenOnly.Click += delegate { cfg.screenOnly = miScreenOnly.IsChecked; cfg.Save(); ApplyScreenOnly(); };
            grpGame.Items.Add(miScreenOnly);
            menu.Items.Add(grpGame);

            MenuItem miSize = new MenuItem(); miSize.Header = "大小";
            MenuItem miBigger = new MenuItem(); miBigger.Header = "放大";
            miBigger.Click += delegate { SetScale(cfg.spriteWidth + 24); };
            MenuItem miSmaller = new MenuItem(); miSmaller.Header = "缩小";
            miSmaller.Click += delegate { SetScale(cfg.spriteWidth - 24); };
            miSize.Items.Add(miBigger); miSize.Items.Add(miSmaller);
            grpLook.Items.Add(miSize);

            MenuItem miReset = new MenuItem(); miReset.Header = "回到右下角";
            miReset.Click += delegate { cfg.x = -1; cfg.y = -1; ApplyPosition(); cfg.Save(); };
            grpLook.Items.Add(miReset);

            miTopmost = new MenuItem(); miTopmost.Header = "总在最前"; miTopmost.IsCheckable = true; miTopmost.IsChecked = cfg.topmost;
            miTopmost.Click += delegate { cfg.topmost = miTopmost.IsChecked; win.Topmost = cfg.topmost; cfg.Save(); };
            grpLook.Items.Add(miTopmost);
            MenuItem miBubbles = new MenuItem();
            miBubbles.Header = "气泡特效";
            miBubbles.IsCheckable = true; miBubbles.IsChecked = cfg.bubbles;
            miBubbles.Click += delegate { cfg.bubbles = miBubbles.IsChecked; cfg.Save(); if (cfg.bubbles) bubbleNext = DateTime.MinValue; };
            menu.Items.Add(miSway);            // 动效开关：摇摆呼吸 + 气泡特效放在一起（用户要求）
            menu.Items.Add(miBubbles);

            if (PetApp.DevMenu)   // 开发者项只在使用 --dev 启动时出现（普通用户看不到）
            {
            // ---- 开发者选项：强制"自动状态"（用户要求：可多切 待机/游戏/打瞌睡）----
            MenuItem miDev = new MenuItem(); miDev.Header = "开发者：自动状态";
            string[] dk = new string[] { null, "idle", "gaming", "doze", "interrupted" };
            string[] dl = new string[] { "跟随计时（默认）", "置于：待机", "置于：游戏", "置于：打瞌睡", "置于：被打断（惊讶脸）" };
            for (int i = 0; i < dk.Length; i++)
            {
                MenuItem mi = new MenuItem();
                mi.Header = dl[i];
                mi.IsCheckable = true;
                mi.Tag = dk[i];
                mi.Click += delegate(object s, RoutedEventArgs e)
                {
                    MenuItem m = (MenuItem)s;
                    foreach (object o in miDev.Items) { MenuItem mm = o as MenuItem; if (mm != null) mm.IsChecked = (mm == m); }
                    string v = Convert.ToString(m.Tag);
                    if (v.Length == 0) { devHoldUntil = DateTime.MinValue; PetConfig.Log("dev: 恢复跟随计时"); UpdateScene(); }
                    else ForceAutoState(v);      // 直接把她放进该状态（走真实链路，之后继续按规则演化）
                };
                miDev.Items.Add(mi);
            }
            grpLook.Items.Add(miDev);

            // ---- 开发者：完整演一遍"被叫去干活"（打断 + 惊讶脸 + 暂停条 + 关屏 + 冥想）----
            MenuItem miWorkDemo = new MenuItem();
            miWorkDemo.Header = "开发者：演一遍『被打断』";
            miWorkDemo.Click += delegate { RunInterruptDemo(); };
            grpLook.Items.Add(miWorkDemo);
            }
            menu.Items.Add(grpLook);

            menu.Items.Add(new Separator());
            MenuItem miAbout = new MenuItem(); miAbout.Header = "关于 / 状态（v" + PetApp.Version + "）";
            miAbout.Click += delegate { Say("DeepSeek 桌宠 v" + PetApp.Version + " · 余额接口：" + cfg.stateUrl, 8); };
            menu.Items.Add(miAbout);
            MenuItem miQuit = new MenuItem(); miQuit.Header = "退出桌宠";
            miQuit.Click += delegate { cfg.Save(); if (tray != null) { tray.Visible = false; tray.Dispose(); } win.Close(); };
            menu.Items.Add(miQuit);

            // 菜单每次打开时，把"跟着场景（自动）"这一项写成**当前实际会落到哪个状态** ——
            // 很多混淆来自"选了自动却还在打游戏"：那其实是游戏屏=常开（她一直在玩）导致的。
            // 「现在：X」提示已移除（用户要求：菜单不写当前状态）

            win.ContextMenu = menu;
            if (!NoTray) SetupTray();
        }

        void SetupTray()
        {
            try
            {
                System.Drawing.Bitmap bmp = LoadDrawingBitmap(Path.Combine(assetsDir, "idle.png"));
                if (bmp == null) return;
                int size = Math.Min(bmp.Width, bmp.Height);
                System.Drawing.Bitmap crop = new System.Drawing.Bitmap(32, 32);
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(crop))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, new System.Drawing.Rectangle(0, 0, 32, 32),
                        new System.Drawing.Rectangle(0, 0, bmp.Width, (int)(bmp.Height * 0.34)),
                        System.Drawing.GraphicsUnit.Pixel);
                }
                tray = new WinForms.NotifyIcon();
                tray.Icon = System.Drawing.Icon.FromHandle(crop.GetHicon());
                tray.Text = "DeepSeek 桌宠";
                tray.Visible = true;
                WinForms.ContextMenuStrip cs = new WinForms.ContextMenuStrip();
                cs.Items.Add("显示 / 隐藏", null, delegate { win.Visibility = win.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; });
                cs.Items.Add("立即刷新余额", null, delegate { RefreshNow(true); });
                cs.Items.Add("退出", null, delegate { Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate { cfg.Save(); tray.Visible = false; win.Close(); })); });
                tray.ContextMenuStrip = cs;
                tray.DoubleClick += delegate { win.Visibility = Visibility.Visible; };
                crop.Dispose(); bmp.Dispose();
            }
            catch (Exception ex) { PetConfig.Log("tray failed: " + ex.Message); }
        }

        /// <summary>加载一张图片为冻结的 BitmapImage（失败返回 null，不抛）</summary>
        static BitmapImage LoadBitmap(string p)
        {
            try
            {
                if (!File.Exists(p)) { PetConfig.Log("missing image: " + p); return null; }
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(p);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex) { PetConfig.Log("load image failed " + p + ": " + ex.Message); return null; }
        }

        static System.Drawing.Bitmap LoadDrawingBitmap(string path)        {
            try { using (System.Drawing.Image im = System.Drawing.Image.FromFile(path)) { return new System.Drawing.Bitmap(im); } }
            catch { return null; }
        }

        // ================= 素材 =================
        void LoadSprites()
        {
            string[] names = new string[] { "idle", "blink", "worry", "sleepy" };   // "happy"（比耶）已退役：风格与其它立绘差距过大
            foreach (string n in names)
            {
                string p = Path.Combine(assetsDir, n + ".png");
                try
                {
                    if (!File.Exists(p)) { PetConfig.Log("missing sprite: " + p); continue; }
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(p);
                    bi.EndInit();
                    bi.Freeze();
                    moods[n] = bi;
                }
                catch (Exception ex) { PetConfig.Log("load sprite " + n + " failed: " + ex.Message); }
            }
            if (moods.ContainsKey("idle")) imgBase.Source = moods["idle"];
            else if (moods.Count > 0) foreach (BitmapImage b in moods.Values) { imgBase.Source = b; break; }
        }

        // ================= 动画 =================
        void StartAnimation()
        {
            tfTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            tfRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            tfScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            animStart = DateTime.Now;
            // 呼吸/摇摆改成 9 FPS 的定时器驱动（原来是 60 FPS 的 WPF 常驻动画）。
            // 慢周期正弦用 9 FPS 采样肉眼看不出差别，但分层窗口的重绘次数降到 1/7。
            if (idleTimer == null)
            {
                idleTimer = new DispatcherTimer();
                idleTimer.Interval = TimeSpan.FromMilliseconds(110);
                idleTimer.Tick += delegate { IdleTick(); };
            }
            idleTimer.Start();
            if (blinkTimer == null)
            {
                blinkTimer = new DispatcherTimer();
                blinkTimer.Interval = TimeSpan.FromSeconds(2.5);
                blinkTimer.Tick += delegate { BlinkTick(); };
            }
            blinkTimer.Start();
            // 「海底气泡」功能已整体删除（用户：脚底一圈泡泡观感不佳）——连计时器与生成函数一起移除
        }

        /// <summary>待机动画：呼吸 + 摇摆 + 轻微缩放（9 FPS 足够，见 StartAnimation 注释）</summary>
        void IdleTick()
        {
            UpdateParticles(0.11);
            // 过渡帧到点后落到正式姿态（用户要的"1-2 帧过渡"）
            if (poseTransUntil != DateTime.MinValue && DateTime.Now >= poseTransUntil)
            {
                poseTransUntil = DateTime.MinValue;
                ApplyPoseSprite(true);
                if (bootPending) { bootPending = false; PlayBootAnim(); }   // 过渡结束 → 开机
            }
            // 打瞌睡：定时吐 Zzz（纯代码特效）
            if (pose == "doze" && DateTime.Now >= zzzNext)
            {
                zzzNext = DateTime.Now.AddMilliseconds(900 + rnd.Next(700));
                SpawnZzz();
            }
            // 常驻气泡：4-9 秒来一串（脚底扬尘已删除）
            if (cfg.bubbles && DateTime.Now >= bubbleNext)
            {
                bubbleNext = DateTime.Now.AddMilliseconds(4000 + rnd.Next(5000));
                SpawnBubbles();
            }
            if (!cfg.sway) return;
            if (scene == "interrupted") return;
            if (bounceUntil > DateTime.Now) return;      // 蹦跳期间交给关键帧动画
            // （"叹气落座"过渡已移到基准赋值之后 —— 放在这里会被下面的赋值覆盖，属无效代码，已删）
            double t = (DateTime.Now - animStart).TotalSeconds;
            if (pose == "doze")
            {
                // 打瞌睡微动作（v0.2.2）：**同一张立绘**做 transform，绝不动素材、也不叠别的图 ——
                // 缓慢呼吸 + 轻晃，另外每约 12 秒一次"猛地点头"（高次幂把正弦压成尖峰），读出"困得直点头"
                double nod = Math.Pow(Math.Max(0.0, Math.Sin(t * 0.52)), 10);
                tfTranslate.Y = -2.0 + 2.0 * Math.Sin(t * 0.90) + 4.0 * nod;
                tfRotate.Angle = 2.2 * Math.Sin(t * 0.90) + 3.2 * nod;
                tfScale.ScaleY = 1.0 + 0.022 * (0.5 + 0.5 * Math.Sin(t * 0.75));   // 睡着的呼吸更慢更深
            }
            else
            {
                tfTranslate.Y = -5.0 + 5.0 * Math.Sin(t * 2.094);                 // 3.0s 周期上下浮动
                tfRotate.Angle = 1.8 * Math.Sin(t * 1.309);                       // 4.8s 周期左右摇摆
                tfScale.ScaleY = 1.0 + 0.015 * (0.5 + 0.5 * Math.Sin(t * 2.094)); // 同步呼吸
            }
            // ⚠ "叹气落座"过渡必须放在**基准值之后**叠加（放在前面会被上面的赋值覆盖 —— 我第一版就写错了位置，
            // 所以实机看起来仍是突变）
            if (settleUntil > DateTime.Now)
            {
                double sp = 1.0 - (settleUntil - DateTime.Now).TotalMilliseconds / 400.0;
                if (sp < 0) sp = 0; if (sp > 1) sp = 1;
                double dip = Math.Sin(sp * Math.PI);         // 0 → 1 → 0
                tfTranslate.Y += 7.0 * dip;
                tfScale.ScaleY *= 1.0 - 0.02 * dip;
            }
        }

        void BlinkTick()
        {
            if (!running) return;
            blinkTimer.Interval = TimeSpan.FromSeconds(2.6 + rnd.NextDouble() * 3.6);
            // v0.2.0：非 idle 姿态下**禁止眨眼层** —— 它是一张"站立立绘"，会整张盖住姿态图
            // （上一轮实机里 handheld 姿态看不见，就是它在作祟）。顺便把可能残留的那一层清掉。
            if (pose != "idle")
            {
                if (imgBlink.Source != null || imgBlink.Opacity > 0.01)
                {
                    imgBlink.BeginAnimation(UIElement.OpacityProperty, null);
                    imgBlink.Opacity = 0;
                    imgBlink.Source = null;
                }
                return;
            }
            // （原海底气泡的启停逻辑已随功能删除）
            if (blinkBusy || DateTime.Now < suspendBlinkUntil) return;
            if (currentMood == "blink") return;
            // ⚠ 姿态层占着立绘时**绝不能眨眼**：blink.png 是"站立闭眼版"，叠在打瞌睡/掌机等姿态图上
            // 会变成"闭眼立绘来回闪"（用户报的 困倦⇄静态闭眼 死循环就是这个）
            if (pose != "idle") return;
            // ⚠ blink.png 是"站立立绘的闭眼版"：在比耶/担心/困倦等其它表情下眨眼，
            // 会突然叠上一张站着的脸 → 看起来像"比耶到一半跳回站立"（用户报的 bug）。
            // 所以只在普通 idle 表情下眨眼。
            if (currentMood != "idle") return;
            if (!moods.ContainsKey("blink")) return;
            blinkBusy = true;
            // 【眨眼：不做整层透明度动画】分层窗口（AllowsTransparency）里对整层跑 Opacity 关键帧时，
            // WPF 每帧都要重算整窗 alpha → 用户实测：眨眼瞬间背景多出一道黑影。
            // 素材本身没问题（blink.png 与 idle.png 同为 720x1090，平均亮度仅差 0.8）。
            // 改为直接替换底图 Source：一次位图切换（无动画、无 alpha 重算），110ms 即闭眼睁眼。
            ImageSource prevBlinkBase = imgBase.Source;
            imgBase.Source = moods["blink"];
            if (blinkResetTimer == null)
            {
                blinkResetTimer = new DispatcherTimer();
                blinkResetTimer.Interval = TimeSpan.FromMilliseconds(110);
                blinkResetTimer.Tick += delegate
                {
                    blinkResetTimer.Stop();
                    // 只在仍是 idle 待机时还原，避免覆盖期间发生的姿态/表情切换
                    if (pose == "idle" && currentMood == "idle" && prevBlinkBase != null) imgBase.Source = prevBlinkBase;
                    blinkBusy = false;
                };
            }
            blinkResetTimer.Stop();
            blinkResetTimer.Start();
        }

        // ================= 姿态集（v0.2.0）=================
        /// <summary>加载 assets\poses\ 下的姿态图与过渡帧；缺素材不算错（自动回退到站立立绘）</summary>
        void LoadPoses()
        {
            try
            {
                string dir = Path.Combine(assetsDir, "poses");
                int n = 0, t = 0;
                foreach (string name in PoseNames)
                {
                    string p = Path.Combine(dir, name + ".png");
                    if (File.Exists(p)) { poses[name] = LoadBitmap(p); n++; }
                    string pt = Path.Combine(dir, name + "_t1.png");
                    if (File.Exists(pt)) { poseTrans[name] = LoadBitmap(pt); t++; }
                }
                PetConfig.Log("poses: " + n + "/" + PoseNames.Length + " loaded, transitions=" + t);
                LoadBeamAnchor();
            }
            catch (Exception ex) { PetConfig.Log("load poses failed: " + ex.Message); }
        }

        /// <summary>读当前姿态的设备锚点 json（没有就用经验值）——光柱要跟着掌机走</summary>
        void LoadBeamAnchor()
        {
            beamAnchorTop = 0.60; beamAnchorW = 0.24; beamAnchorCx = 0.5;
            try
            {
                string f = Path.Combine(Path.Combine(assetsDir, "poses"), pose + ".json");
                if (!File.Exists(f)) { PetConfig.Log("beam anchor: no json for pose=" + pose); return; }
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                if (d.ContainsKey("deviceTop")) beamAnchorTop = Convert.ToDouble(d["deviceTop"]);
                if (d.ContainsKey("deviceWidth")) beamAnchorW = Convert.ToDouble(d["deviceWidth"]);
                if (d.ContainsKey("deviceCenterX")) beamAnchorCx = Convert.ToDouble(d["deviceCenterX"]);
                PetConfig.Log("beam anchor(" + pose + "): top=" + beamAnchorTop.ToString("F3")
                              + " w=" + beamAnchorW.ToString("F3") + " cx=" + beamAnchorCx.ToString("F3"));
            }
            catch (Exception ex) { PetConfig.Log("beam anchor failed: " + ex.Message); }
        }

        /// <summary>交叉淡入到指定位图（复用换表情那套 imgBase/imgFade 机制）</summary>
        void CrossFadeTo(BitmapImage target, int ms)
        {
            if (target == null) return;
            if (ms <= 0 || imgBase.Source == null)
            {
                imgBase.BeginAnimation(UIElement.OpacityProperty, null);
                imgFade.BeginAnimation(UIElement.OpacityProperty, null);
                imgBase.Source = target; imgBase.Opacity = 1; imgFade.Opacity = 0; imgFade.Source = null;
                return;
            }
            BitmapImage want = target;
            imgFade.Source = want;
            DoubleAnimation fi = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms));
            DoubleAnimation fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms));
            fi.Completed += delegate
            {
                imgBase.BeginAnimation(UIElement.OpacityProperty, null);
                imgBase.Source = want;
                imgBase.Opacity = 1;
                imgFade.BeginAnimation(UIElement.OpacityProperty, null);
                imgFade.Opacity = 0;
                imgFade.Source = null;
            };
            imgFade.BeginAnimation(UIElement.OpacityProperty, fi);
            imgBase.BeginAnimation(UIElement.OpacityProperty, fo);
        }

        /// <summary>切姿态：有过渡帧就先播 ~170ms（用户要的"1-2 帧过渡"），再落到正式姿态</summary>
        void SetPose(string next, bool animate)
        {
            // 开发用：--posetest 时锁死在该姿态（场景机不要抢）
            // 菜单锁定的姿态也不能挡住"被拎起"（否则站着待机时拖她毫无反应）
            if (PetApp.PoseTest.Length > 0 && next != PetApp.PoseTest && next != "pick") return;
            // 拖动期间锁死在 pick：场景机不许把姿态切回掌机（否则会闪出"举机"过渡帧）
            if (dragging && next != "pick") return;
            if (next == pose) return;
            pose = next;
            LoadBeamAnchor();   // ⚠ 必须在这里（过渡帧分支会提前 return，放在后面就永远读不到 json）
            BitmapImage trans = null;
            if (poseTrans.TryGetValue(next, out trans) && trans != null && animate && !skipTrans)
            {
                SetSpriteNaturalSize();
                CrossFadeTo(trans, 90);
                poseTransUntil = DateTime.Now.AddMilliseconds(170);
                bootPending = true;
                // ⚠ 一律用 Opacity，不能 Collapsed：屏幕在 Auto 行，塌行会把立绘顶上去 180 DIP
                if (screenBezel != null) screenBezel.Opacity = 0;
                HideBeam();
                PetConfig.Log("pose=" + next + " (transition frame, device off)");
                return;
            }
            // 过渡帧已退役（用户要求删除"掏出掌机"那帧：它制造了太多半状态 bug）。
            // 进掌机姿态时仍按"设备关机 → 170ms 后开机"的节奏走，但直接用正式姿态，不再有中间帧。
            if (next == "handheld" && animate && !skipTrans)
            {
                bootPending = true;
                if (screenBezel != null) screenBezel.Opacity = 0;
                HideBeam();
                poseTransUntil = DateTime.Now.AddMilliseconds(170);
                ApplyPoseSprite(true);
                PetConfig.Log("pose=handheld (boot in 170ms, no transition frame)");
                return;
            }
            poseTransUntil = DateTime.MinValue;
            ApplyPoseSprite(animate);
            LoadBeamAnchor();   // 换姿态就换设备锚点（过渡帧里她把掌机举到头顶，锚点完全不同）
            PetConfig.Log("pose=" + next + (poses.ContainsKey(next) ? "" : " (no art -> standing fallback)"));
        }

        /// <summary>站立立绘按素材原始尺寸显示（姿态集与它同画布，所以尺寸不变）</summary>
        void SetSpriteNaturalSize()
        {
            imgBase.Width = double.NaN; imgBase.Height = double.NaN;
            imgFade.Width = double.NaN; imgFade.Height = double.NaN;
        }

        /// <summary>
        /// 取某姿态实际要显示的图。**被打断（interrupted）时，掌机姿态换成"惊讶+白眼"变体**（用户要求：
        /// 被叫去干活要有表情变化）—— 该变体与 handheld 同身体同构图，只换脸，所以切换不会跳动。
        /// </summary>
        BitmapImage PoseSpriteFor(string p)
        {
            if (p == "handheld" && scene == "interrupted" && poses.ContainsKey("handheld_surprise"))
                return poses["handheld_surprise"];
            BitmapImage img = null;
            poses.TryGetValue(p, out img);
            return img;
        }

        /// <summary>
        /// 姿态切换的"渐变"过渡（用户要求）：先淡到 30%（160-260ms）→ 换图 → 再淡回 100%（600ms）。
        /// 比单纯交叉淡入更有渐变感，也避免两张体型差异大的立绘互相透出（看起来像跳变）。
        /// </summary>
        void FadeThroughTo(BitmapImage want, int outMs, int inMs)
        {
            try
            {
                if (want == null) return;
                if (imgFade != null) { imgFade.BeginAnimation(UIElement.OpacityProperty, null); imgFade.Opacity = 0; imgFade.Source = null; }
                fadingThrough = true;
                double from = imgBase.Opacity <= 0.05 ? 1.0 : imgBase.Opacity;
                DoubleAnimation fadeOut = new DoubleAnimation(from, 0.3, TimeSpan.FromMilliseconds(outMs));
                fadeOut.Completed += delegate
                {
                    imgBase.Source = want;
                    fadingThrough = false;      // 换图完成，交还给不变量
                    imgBase.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(inMs)));
                    PetConfig.Log("pose fade-through -> " + pose);
                };
                imgBase.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            catch (Exception ex) { PetConfig.Log("fade-through failed: " + ex.Message); imgBase.Source = want; imgBase.Opacity = 1; fadingThrough = false; }
        }

        void ApplyPoseSprite(bool animate)
        {
            // 切姿态时把眨眼层清干净（否则站立立绘会压在姿态图上）
            if (imgBlink != null && (imgBlink.Source != null || imgBlink.Opacity > 0.01))
            {
                imgBlink.BeginAnimation(UIElement.OpacityProperty, null);
                imgBlink.Opacity = 0;
                imgBlink.Source = null;
            }
            BitmapImage want = null;
            if (pose != "idle") want = PoseSpriteFor(pose);
            if (want == null)
            {
                // 姿态回到站立：**先**刷新表情（此刻 pose 已是 idle，ApplyMood 会真正执行），再按它取图。
                // 否则会按"旧表情"画一帧（例如刚从打瞌睡醒来时先画出困倦立绘）——用户报的来回切就是这个。
                ApplyMood(false);
                string m = (currentMood != null && moods.ContainsKey(currentMood)) ? currentMood : "idle";
                if (!moods.ContainsKey(m)) m = "idle";
                moods.TryGetValue(m, out want);
            }
            SetSpriteNaturalSize();
            int ms = nextFadeMs > 0 ? nextFadeMs : (animate ? 320 : 0);
            nextFadeMs = 0;
            if (ms > 0) FadeThroughTo(want, Math.Max(160, ms / 3), ms); else CrossFadeTo(want, 0);
        }

        /// <summary>场景 → 姿态：gaming=拿掌机玩、doze=打瞌睡、work=盘腿冥想（深度求索）、其余=站立待机</summary>
        string PoseForScene()
        {
            if (scene == "doze") return "doze";
            if (scene == "work") return "meditate";
            // 被打断时她**仍捧着掌机**（只是被吓得表情变了）—— 所以姿态与 gaming 相同，
            // 再由 PoseSpriteFor() 换成"惊讶+白眼"变体。此前这里返回 idle 会让开发者选项下
            // 看到她"吓得站起来"，与真实链路（直接置 scene）不一致。
            if (scene == "gaming" || scene == "interrupted") return "handheld";
            return "idle";
        }

        /// <summary>在她身边招一只苦力怕（纯代码像素画，**不打开游戏屏**）：从一侧走进来、走到另一侧、淡出</summary>
        void SpawnPetCreeper()
        {
            try
            {
                if (particleLayer == null) return;
                int W = 26, H = 34;
                int[] buf = new int[W * H];
                Px.Creeper(buf, W, H, 6, H - 2, rnd.Next(3));       // 复用游戏屏那套像素画
                WriteableBitmap wb = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, W, H), buf, W * 4, 0);
                Image img = new Image();
                img.Source = wb;
                img.Width = W * 2.6; img.Height = H * 2.6;
                img.IsHitTestVisible = false;
                bool fromLeft = rnd.Next(2) == 0;
                double span = Math.Max(140, win.ActualWidth);
                double x0 = fromLeft ? -70 : span + 10;
                double x1 = fromLeft ? span + 10 : -70;
                img.Opacity = 0;
                Canvas.SetLeft(img, x0); Canvas.SetTop(img, 170 + rnd.Next(140));
                particleLayer.Children.Add(img);
                DoubleAnimation move = new DoubleAnimation(x0, x1, TimeSpan.FromSeconds(2.8));
                DoubleAnimationUsingKeyFrames fade = new DoubleAnimationUsingKeyFrames();
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.8)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                move.Completed += delegate { particleLayer.Children.Remove(img); };
                img.BeginAnimation(Canvas.LeftProperty, move);
                img.BeginAnimation(UIElement.OpacityProperty, fade);
                SayKey(fromLeft ? "呀，有苦力怕！" : "背后有嘶嘶声…", 2.4);   // 彩蛋：插队
                SetMood("worry", false);
                moodHoldUntil = DateTime.Now.AddSeconds(2.2);       // 别被 ApplyMood 立刻刷回
                PetConfig.Log("pet creeper spawned beside her (screen untouched)");
            }
            catch (Exception ex) { PetConfig.Log("pet creeper failed: " + ex.Message); }
        }

        /// <summary>把余额换算成"能买几份红烧肉"（她最喜欢吃的东西）</summary>
        static int PorkCount(double total)
        {
            if (double.IsNaN(total) || total <= 0) return 0;
            return (int)Math.Floor(total / PorkPrice);
        }

        /// <summary>点击回应：从她头顶冒出几颗四角星闪光（纯代码路径几何，不需要素材）</summary>
        void SpawnClickHearts()
        {
            try
            {
                if (particleLayer == null) return;
                int n = 4 + rnd.Next(3);
                for (int i = 0; i < n; i++)
                {
                    System.Windows.Shapes.Path star = new System.Windows.Shapes.Path();
                    // 四角星（细长尖角 + 内凹）
                    star.Data = Geometry.Parse("M 8,0 L 9.6,6.4 L 16,8 L 9.6,9.6 L 8,16 L 6.4,9.6 L 0,8 L 6.4,6.4 Z");
                    double sz = 0.5 + rnd.NextDouble() * 0.9;
                    star.RenderTransform = new ScaleTransform(sz, sz);
                    star.RenderTransformOrigin = new Point(0.5, 0.5);
                    star.Fill = new SolidColorBrush(Color.FromArgb(240, 255, 246, 200));   // 暖白偏金
                    // 轻微旋转让每颗星姿态不同（闪光感）
                    TransformGroup tg = new TransformGroup();
                    tg.Children.Add(new RotateTransform(rnd.Next(-30, 30), 8, 8));
                    tg.Children.Add(new ScaleTransform(sz, sz));
                    star.RenderTransform = tg;
                    star.IsHitTestVisible = false;
                    star.Opacity = 0;   // ⚠ 同 DustBurst：必须从透明开始，否则会先闪出一帧全不透明贴图
                    Particle p = new Particle();
                    p.x = spriteStack.ActualWidth / 2 + (rnd.NextDouble() - 0.5) * 84 - 8 * sz;
                    p.y = 30 + rnd.Next(60);                       // 头顶一带
                    p.vx = (rnd.NextDouble() - 0.5) * 34;
                    p.vy = -(26 + rnd.NextDouble() * 30);          // 向上飘
                    p.life0 = p.life = 0.75 + rnd.NextDouble() * 0.55;
                    p.shape = star;
                    Canvas.SetLeft(star, p.x); Canvas.SetTop(star, p.y);
                    particleLayer.Children.Add(star);
                    parts.Add(p);
                }
                PetConfig.Log("click sparkles: " + n);
            }
            catch (Exception ex) { PetConfig.Log("click sparkles failed: " + ex.Message); }
        }

        /// <summary>
        /// 单击她之后的全部反应（真实点击与开发用 --clicktest 共用同一条路径）。
        /// 语义：单击＝把她叫回来 → 重置场景计时、结束开发者观察期、蹦跳 + 头顶星星 + 按"点击后的状态"选台词。
        /// </summary>
        void OnPetted()
        {
            lastSceneReset = DateTime.Now;
            lastUserAction = DateTime.Now;   // ⚠ 必须一起重置：ForceAutoState 曾把互动时间伪造到 600 秒前，
                                             // 不重置的话点击后睡眠计时立刻又把她判成打瞌睡（=卡在困倦，用户报的 bug）
            devHoldUntil = DateTime.MinValue;
            Bounce();
            SpawnClickHearts();
            suspendBlinkUntil = DateTime.Now.AddSeconds(1.5);
            string line;
            // 深度求索（冥想/干活）时点她：说这一套，而不是日常台词（用户反馈）
            if (scene == "work" || PetApp.PoseTest == "meditate") line = WorkLines[rnd.Next(WorkLines.Length)];
            else if (PetApp.PoseTest == "handheld" || scene == "gaming" || scene == "interrupted") line = GamingClickLine();   // 正常玩也走游戏台词（修：此前只有开发者开关才生效）
            else if (PetApp.PoseTest == "doze") line = DozeLines[rnd.Next(DozeLines.Length)];
            // 被拎着（拖动中或菜单锁定"被拎起来"）时点她 → 继续抗议（此前漏了这一档，落到了日常台词）
            else if (PetApp.PoseTest == "pick" || pose == "pick") line = PickLines[rnd.Next(PickLines.Length)];
            else if (pose == "doze" || scene == "doze")
            {
                // 睡着被叫醒：先保持打瞌睡姿态 2.5s（"迷糊"感），随后自然回到待机。
                // ⚠ 不要再 SetMood("sleepy")：sleepy.png 是**站立的打哈欠立绘**，姿态回到待机的那一瞬
                // 会先画"站立困倦脸"再切"站立微笑脸"，两次连续换图 = 用户看到的"困倦与待机来回切"。
                line = WakeLines[rnd.Next(WakeLines.Length)];
                dozeWakeUntil = DateTime.Now.AddSeconds(2.5);
                moodHoldUntil = DateTime.Now.AddSeconds(2.5);
                PetConfig.Log("pet wake: 打盹缓冲 2.5s（scene=" + scene + " pose=" + pose + "）");
            }
            else line = Lines[rnd.Next(Lines.Length)];
            PetConfig.Log("pet click: scene=" + scene + " pose=" + pose + " → line=" + line);
            SayKey(line, 4);   // 用户主动交互优先：插队而不是丢弃
        }

        /// <summary>
        /// 开发用（--uitest）：**逐个调用"用户操作"背后的同一个处理函数**（不是注入状态、也不是模拟鼠标坐标），
        /// 覆盖：单击 / 点屏幕换游戏 / 置于四态 / 打断演出 / 滚轮缩放 / 招苦力怕彩蛋。
        /// 每步 3.5 秒并写日志；跑完用日志核对每一步的场景数、姿态与台词。
        /// </summary>
        void RunUiTest()
        {
            string[] steps = new string[] {
                "click", "cycleGame", "autoscene:gaming", "click", "autoscene:doze",
                "workDemo", "cycleGame", "click", "creeper", "scaleUp", "scaleDown", "autoscene:interrupted"
            };
            int i = 0;
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(3500);
            t.Tick += delegate
            {
                if (i >= steps.Length) { t.Stop(); PetConfig.Log("uitest: done"); return; }
                string s = steps[i];
                PetConfig.Log("uitest: step " + i + " = " + s);
                try
                {
                    if (s == "click") OnPetted();
                    else if (s == "cycleGame") CycleGame();
                    else if (s == "creeper") { if (screenVisible && mc != null) mc.TriggerCreeper(); else SpawnPetCreeper(); }
                    else if (s == "scaleUp") SetScale(cfg.spriteWidth + 24);
                    else if (s == "scaleDown") SetScale(cfg.spriteWidth - 24);
                    else if (s == "workDemo") RunInterruptDemo();
                    else if (s.StartsWith("autoscene:")) ForceAutoState(s.Substring(10));
                }
                catch (Exception ex) { PetConfig.Log("uitest step failed: " + ex.Message); }
                i++;
            };
            t.Start();
        }
        /// <summary>
        /// 开发者自检（--menutest）：遍历右键菜单树，核对**关键菜单项是否齐全、文字是否为空**。
        /// 为什么需要：--uitest 调用的是"处理函数"，测不到"菜单项没挂上/文字被改坏/项被误删"这类装配问题。
        /// </summary>
        void CheckMenu()
        {
            if (menu == null) { PetConfig.Log("menutest: menu 未创建（可能带 --nomenu）"); return; }
            System.Collections.Generic.List<string> all = new System.Collections.Generic.List<string>();
            int emptyHeader = 0;
            Action<System.Windows.Controls.ItemCollection> walk = null;
            walk = delegate(System.Windows.Controls.ItemCollection col)
            {
                foreach (object o in col)
                {
                    MenuItem mi = o as MenuItem;
                    if (mi == null) continue;                     // Separator 等
                    string h = Convert.ToString(mi.Header);
                    if (string.IsNullOrEmpty(h)) { emptyHeader++; PetConfig.Log("menutest: 发现空文字菜单项"); }
                    else all.Add(h);
                    if (mi.Items.Count > 0) walk(mi.Items);
                }
            };
            walk(menu.Items);
            string[] requiredBase = new string[] {
                "立即刷新余额", "显示余额徽章",
                "让她做什么（状态）", "表情", "摇摆", "气泡特效",
                "游戏屏", "招一只苦力怕（彩蛋）", "只看游戏屏（挂机模式）",
                "大小", "回到右下角", "总在最前",
                // 开发者项只在带 --dev 时要求存在（普通用户菜单里没有）
                "退出桌宠"
            };
            System.Collections.Generic.List<string> requiredList = new System.Collections.Generic.List<string>();
            foreach (string r0 in requiredBase) requiredList.Add(r0);
            if (PetApp.DevMenu) { requiredList.Add("开发者：自动状态"); requiredList.Add("开发者：演一遍『被打断』"); }
            int missing = 0;
            foreach (string r in requiredList)
            {
                bool found = false;
                foreach (string h in all) if (h.StartsWith(r)) { found = true; break; }
                if (!found) { missing++; PetConfig.Log("menutest: 缺菜单项 → " + r); }
            }
            // ---- 菜单项**有效性**自检（用户要求）：菜单项"在"不等于"有用" ----
            // 逐个设置游戏屏三档，核对 screenVisible 是否符合预期（此前"关闭"点了没反应就是这类 bug）
            string savedMode = cfg.screenMode;
            bool savedVis = screenVisible;
            string[] modes = new string[] { "on", "off", "auto" };
            for (int mi2 = 0; mi2 < modes.Length; mi2++)
            {
                cfg.screenMode = modes[mi2];
                UpdateScene();
                bool expect = modes[mi2] == "on" ? true : (modes[mi2] == "off" ? false : screenVisible);
                bool pass = modes[mi2] == "auto" ? true : (screenVisible == expect);
                PetConfig.Log("menutest: 游戏屏=" + modes[mi2] + " → screenVisible=" + screenVisible + (pass ? " ✓" : " ✗ 不符合预期（菜单项无效）"));
                if (!pass) missing++;
            }
            cfg.screenMode = savedMode; screenVisible = savedVis; UpdateScene();            PetConfig.Log("menutest: 共 " + all.Count + " 项 · 关键项缺 " + missing + " · 空文字 " + emptyHeader);
            Say(missing == 0 && emptyHeader == 0 ? "菜单自检通过～" : ("菜单自检：缺 " + missing + " 项，看日志"), 4);
        }
        /// <summary>
        /// 开发者（--balancetest）：**注入伪余额快照**跑一遍播报分支 —— 扣费 / 充值 / 低额 / 连接失败。
        /// 为什么需要：--uitest 走的是用户操作，测不到"余额变化"这条由轮询驱动的事件链。
        /// </summary>
        void RunBalanceTest()
        {
            PetSnapshot a = new PetSnapshot();
            a.ok = true; a.isAvailable = true; a.total = 50.00; a.currency = "CNY";
            a.updatedAt = DateTime.Now.ToString("HH:mm:ss");
            OnState(a, false);                                    // 建立基准
            PetConfig.Log("balancetest: 1) 基准 ¥50.00");

            DispatcherTimer t = new DispatcherTimer();
            int step = 0;
            t.Interval = TimeSpan.FromSeconds(4.5);
            t.Tick += delegate
            {
                PetSnapshot s = new PetSnapshot();
                s.ok = true; s.isAvailable = true; s.currency = "CNY";
                s.updatedAt = DateTime.Now.ToString("HH:mm:ss");
                switch (step)
                {
                    case 0: s.total = 49.60; PetConfig.Log("balancetest: 2) 扣费 → ¥49.60（应播报+红烧肉随机）"); break;
                    case 1: s.total = 12.00; s.busy = true; PetConfig.Log("balancetest: 3) 降到 ¥12（8-16 档：不太够了 + 播报期担心脸）"); break;
                    case 2: s.total = 5.00; PetConfig.Log("balancetest: 4) 降到 ¥5（<8 档：要没钱了）"); break;
                    case 3: s.total = 60.00; PetConfig.Log("balancetest: 5) 充值 → ¥60（还很多档）"); break;
                    case 4: case 5: case 6: case 7: case 8:   // 连续 5 次失败 → 应触发"拿不到余额"提示
                        s.ok = false; s.isAvailable = false; s.total = double.NaN;
                        PetConfig.Log("balancetest: 连续失败第 " + (step - 3) + " 次（应转灰/不可用）");
                        break;
                    default: t.Stop(); PetConfig.Log("balancetest: done"); return;
                }
                OnState(s, false);
                step++;
            };
            t.Start();
        }
        /// <summary>落地扬尘：短命粒子向两侧炸开（纯代码，不需要素材）</summary>
        void DustBurst()
        {
            PetConfig.Log("particles: dust burst (落地扬尘)");   // 来源审计

            if (particleLayer == null) return;   // 落地扬尘不再受"海底气泡"开关影响（该功能已删除）
            double w = spriteStack.ActualWidth > 0 ? spriteStack.ActualWidth : win.Width;
            double h = spriteStack.ActualHeight > 0 ? spriteStack.ActualHeight : win.Height * 0.7;
            for (int i = 0; i < 8; i++)
            {
                Ellipse e = new Ellipse();
                double size = 3 + rnd.NextDouble() * 5;
                e.Width = size; e.Height = size;
                e.Fill = new SolidColorBrush(Color.FromArgb(255, 188, 192, 208));
                // ⚠ 必须从透明开始：默认 Opacity=1 会让粒子在生成点**以全不透明渲染一帧**
                //（用户报的"pickup 转 idle 瞬间突然出现一帧粒子贴图"就是这个）
                e.Opacity = 0;
                Particle p = new Particle();
                p.x = w * 0.22 + rnd.NextDouble() * w * 0.56;
                p.y = h - 16 - rnd.NextDouble() * 12;   // 纵向也散开，避免 8 颗叠成一个色块
                p.vx = (rnd.NextDouble() - 0.5) * 70;
                p.vy = -(8 + rnd.NextDouble() * 20);
                p.life0 = p.life = 0.45 + rnd.NextDouble() * 0.4;
                p.shape = e;
                Canvas.SetLeft(e, p.x); Canvas.SetTop(e, p.y);
                particleLayer.Children.Add(e);
                parts.Add(p);
            }
        }

        /// <summary>挂机模式：只留纯代码游戏屏（她收起来；右键菜单可切）</summary>
        void ApplyScreenOnly()
        {
            bool on = cfg.screenOnly;
            if (spriteStack != null) spriteStack.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) { cfg.screenMode = "on"; }
            screenOnlyApplied = on;
            ApplyLayout();
            PetConfig.Log("screenOnly=" + on + " win=" + win.Width.ToString("F0") + "x" + win.Height.ToString("F0"));
        }

        /// <summary>收起光柱（举机/关机时）</summary>
        void HideBeam()
        {
            if (beam != null) beam.Visibility = Visibility.Collapsed;
            if (beamBack != null) beamBack.Visibility = Visibility.Collapsed;
            if (beamGlow != null) beamGlow.Visibility = Visibility.Collapsed;
            if (beamEdge != null) beamEdge.Visibility = Visibility.Collapsed;
        }

        /// <summary>布局变更后延迟一拍再显示立绘：把"窗口尺寸/位置变化的瞬时帧"藏掉，彻底避免裁切闪帧</summary>
        void ShowSpriteAfterLayout()
        {
            DispatcherTimer rt = new DispatcherTimer();
            rt.Interval = TimeSpan.FromMilliseconds(90);
            rt.Tick += delegate
            {
                rt.Stop();
                if (spriteStack != null) spriteStack.Opacity = 1;
            };
            rt.Start();
        }

        /// <summary>关游戏动画（v0.2.2）：屏幕像 CRT 关机一样缩放淡出 + 光柱一起淡掉，随后才收起窗口高度。</summary>
        void PlayShutdownAnim(Action afterHide)
        {
            try
            {
                if (screenBezel != null)
                {
                    if (screenScale == null)
                    {
                        screenScale = new ScaleTransform(1, 1);
                        screenBezel.RenderTransformOrigin = new Point(0.5, 0.5);
                        screenBezel.RenderTransform = screenScale;
                    }
                    DoubleAnimation sc = new DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(180));
                    screenScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                    screenScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
                    DoubleAnimation op = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
                    op.Completed += delegate { if (afterHide != null) afterHide(); };
                    screenBezel.BeginAnimation(UIElement.OpacityProperty, op);
                }
                else if (afterHide != null) afterHide();
                // 光柱同步淡出（HideBeam 会立刻收起，这里用动画更自然）
                foreach (FrameworkElement fe in new FrameworkElement[] { beam, beamBack, beamGlow })
                {
                    if (fe == null) continue;
                    fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
                }
                PetConfig.Log("shutdown anim: screen scale-fade + beam fade");
            }
            catch (Exception ex) { PetConfig.Log("shutdown anim failed: " + ex.Message); if (afterHide != null) afterHide(); }
        }

        /// <summary>开机动画：屏幕缩放淡入 + 光柱从 0 淡到满（用户要的"过渡完成再开机"）</summary>
        void PlayBootAnim()
        {
            try
            {
                if (screenBezel != null && screenVisible
                    && (PetApp.PoseTest.Length == 0 || PetApp.PoseTest == "handheld"))
                {
                    if (screenScale == null)
                    {
                        screenScale = new ScaleTransform(1, 1);
                        screenBezel.RenderTransformOrigin = new Point(0.5, 0.5);
                        screenBezel.RenderTransform = screenScale;
                    }
                    DoubleAnimation sc = new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(260));
                    screenScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                    screenScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
                    screenBezel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                    screenBezel.Visibility = Visibility.Visible;
                }
                foreach (FrameworkElement fe in new FrameworkElement[] { beam, beamBack, beamGlow })
                {
                    if (fe == null) continue;
                    fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)));
                }
                PetConfig.Log("boot anim: screen fade-in + beam ramp-up");
            }
            catch (Exception ex) { PetConfig.Log("boot anim failed: " + ex.Message); }
        }

        /// <summary>重算投影光柱的几何：下端贴着她的掌机、上端张开到游戏屏下沿</summary>
        void UpdateBeam()
        {
            if (beam == null) return;
            bool show = !cfg.screenOnly && screenVisible && pose == "handheld" && !bootPending;
            if (PetApp.BeamTest == "on" || PetApp.BeamTest == "debug") show = true;
            if (PetApp.BeamTest == "off") show = false;
            if (DebugMode) PetConfig.Log("beam " + (show ? "ON" : "off") + " pose=" + pose
                + " scr=" + screenVisible + " win=" + win.Width.ToString("F0") + "x" + win.Height.ToString("F0")
                + " imgH=" + (imgBase != null ? imgBase.ActualHeight.ToString("F0") : "-"));
            if (!show)
            {
                // 光柱改成淡出（而不是瞬间消失）：与屏幕淡出同步，避免切换瞬间"闪两下"
                if (beam != null && beam.Opacity > 0.05) FadeVis(beam, false, 260);
                if (beamBack != null) beamBack.Visibility = Visibility.Collapsed;
                if (beamGlow != null) beamGlow.Visibility = Visibility.Collapsed;
                if (beamEdge != null) beamEdge.Visibility = Visibility.Collapsed;
                return;
            }
            double w = win.Width > 0 ? win.Width : 276;
            double h = win.Height > 0 ? win.Height : 383;
            double cx = w / 2;
            // 光柱顶端只到屏幕下沿（屏幕在光柱之上，所以不怕重叠，但保持不侵入）
            double yTop = (screenVisible ? ScreenH + ScreenChrome : 0) - 2;
            // 光柱根 = 掌机（设备）的上边缘：锚点用立绘实际尺寸换算，缩放/换姿态时幅度自动一致
            double yBot = h * 0.52, botW = 54, edgeX = 0, edgeW = 0;
            try
            {
                if (imgBase != null && imgBase.ActualHeight > 0)
                {
                    Point p0 = imgBase.TranslatePoint(new Point(0, 0), root);
                    double imgTop = p0.Y, imgH = imgBase.ActualHeight, imgW = imgBase.ActualWidth;
                    yBot = imgTop + imgH * beamAnchorTop;          // 光的出发点＝掌机上边缘（用户红笔标定，不再上移）
                    double devW = Math.Max(40, imgW * beamAnchorW);            // 设备（含溢光边）宽度
                    botW = Math.Max(30, devW * 0.66);                          // 光柱下口收窄
                    cx = p0.X + imgW * beamAnchorCx;                           // 光柱中心 = 设备中心
                    edgeX = cx - devW / 2; edgeW = devW;
                }
            }
            catch { }
            // 掌机在屏幕**下方**：正常情形就是 yBot > yTop（光从下往上打）。
            // ⚠ 上一版这里把方向写反了（钳成 yBot = yTop-12），整个锥形被压成紧贴屏幕下沿的
            // 一条 14px 横带 —— 用户圈出来问"你确定这是光柱？"的就是它。
            if (yBot < yTop + 16) yBot = yTop + 16;      // 只防"掌机跑到屏幕上方"的极端情况
            // 渐变用绝对坐标跟着几何走：下端（掌机）最亮 → 上端（屏幕）淡出
            beamBrush.StartPoint = new Point(cx, yBot);
            beamBrush.EndPoint = new Point(cx, yTop);
            double topW = Math.Min(w - 16, ScreenW - 26);
            PointCollection pts = new PointCollection();
            pts.Add(new Point(cx - topW / 2, yTop));
            pts.Add(new Point(cx + topW / 2, yTop));
            pts.Add(new Point(cx + botW / 2, yBot));
            pts.Add(new Point(cx - botW / 2, yBot));
            // 羽化遮罩跟着几何走：左边缘→右边缘
            if (beamMask != null)
            {
                beamMask.StartPoint = new Point(cx - topW / 2, 0);
                beamMask.EndPoint = new Point(cx + topW / 2, 0);
            }
            beam.Points = pts;
            // 底部那道光带（beamEdge）已按用户要求撤掉：不要"意义不明的横条"，
            // 改成整条光柱越往越下越淡 → 看不出光的边界在哪里
            if (beamEdge != null) beamEdge.Visibility = Visibility.Collapsed;
            if (beamBack != null) { beamBack.Points = pts; beamBack.Visibility = Visibility.Visible; }
            if (beamGlow != null)
            {
                // 辉光贴在掌机上（覆盖"光源"那一小块），做成横向扁椭圆
                double gw = Math.Max(78, botW * 1.9), gh = gw * 0.62;
                beamGlow.Width = gw; beamGlow.Height = gh;
                beamGlow.Margin = new Thickness(cx - gw / 2, yBot - gh * 0.52, 0, 0);
                beamGlow.Visibility = Visibility.Visible;
            }
            // 光柱里的"微尘"已删除（用户反馈：看起来像她身边飘着的一群泡泡）。
            // 以后若要恢复：必须严格限制在光锥内部、更小更淡，并做成可开关的效果。
            beam.Fill = (PetApp.BeamTest == "debug")
                ? (Brush)new SolidColorBrush(Color.FromArgb(150, 255, 0, 160))   // 调试：实心洋红，看清几何
                : beamBrush;
            beam.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// （原「海底气泡」生成函数 SpawnParticle 已随功能整体删除 —— 脚底一圈泡泡观感不佳）
        /// </summary>

        /// <summary>
        /// 粒子推进：全部在代码里算，由 9 FPS 的待机定时器驱动。
        /// 原先是每个粒子挂 3 条 WPF 常驻动画 —— 那会让分层窗口一直以 60 FPS 重绘，
        /// 实测待机 CPU 从 24% 降到 ~2% 主要就是靠这一处（见 DEVLOG 第 9 节性能记录）。
        /// </summary>
        /// <summary>打瞌睡的 Zzz：向上飘的 "Z" 字，纯代码特效（用户要求不进立绘）</summary>
        void SpawnZzz()
        {
            if (!running || particleLayer == null) return;
            double w = spriteStack.ActualWidth > 0 ? spriteStack.ActualWidth : win.Width;
            double h = spriteStack.ActualHeight > 0 ? spriteStack.ActualHeight : win.Height * 0.7;
            TextBlock t = new TextBlock();
            t.Text = "Z";
            t.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif");
            t.FontWeight = FontWeights.Bold;
            t.FontSize = 15 + rnd.NextDouble() * 11;
            t.Foreground = new SolidColorBrush(Color.FromArgb(235, 226, 238, 255));
            Particle p = new Particle();
            p.x = w * 0.58 + rnd.NextDouble() * w * 0.26;     // 从她头右上方飘
            p.y = h * 0.30 + rnd.NextDouble() * h * 0.08;
            p.vx = 6 + rnd.NextDouble() * 8;
            p.vy = -(10 + rnd.NextDouble() * 7);
            p.life0 = p.life = 1.5 + rnd.NextDouble() * 0.5;
            p.shape = t;
            Canvas.SetLeft(t, p.x); Canvas.SetTop(t, p.y);
            particleLayer.Children.Add(t);
            parts.Add(p);
        }

        /// <summary>
        /// 常驻气泡特效（用户要求重做）：从她**身侧/头顶**缓缓上浮的半透明小水泡，
        /// 4-9 秒一串、每串 2-4 个，向上飘 + 轻微左右摆，自动淡入淡出。
        /// （与已删除的旧"海底气泡"不同：不再从脚底冒，密度也低得多。）
        /// </summary>
        void SpawnBubbles()
        {
            if (!running || particleLayer == null) return;
            double w = spriteStack.ActualWidth > 0 ? spriteStack.ActualWidth : win.Width;
            double h = spriteStack.ActualHeight > 0 ? spriteStack.ActualHeight : win.Height * 0.7;
            int n = 2 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                double d = 5 + rnd.NextDouble() * 9;
                Ellipse e = new Ellipse();
                e.Width = d; e.Height = d;
                e.Fill = new SolidColorBrush(Color.FromArgb(66, 190, 225, 255));
                e.Stroke = new SolidColorBrush(Color.FromArgb(140, 226, 242, 255));
                e.StrokeThickness = 1.1;
                Particle pb = new Particle();
                pb.x = w * (0.18 + rnd.NextDouble() * 0.64);
                pb.y = h * (0.34 + rnd.NextDouble() * 0.46);
                pb.vx = (rnd.NextDouble() - 0.5) * 9;
                pb.vy = -(16 + rnd.NextDouble() * 14);
                pb.life0 = pb.life = 2.0 + rnd.NextDouble() * 1.2;
                pb.shape = e;
                Canvas.SetLeft(e, pb.x); Canvas.SetTop(e, pb.y);
                particleLayer.Children.Add(e);
                parts.Add(pb);
            }
        }
        void UpdateParticles(double dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle p = parts[i];
                p.life -= dt;
                if (p.life <= 0)
                {
                    particleLayer.Children.Remove(p.shape);
                    parts.RemoveAt(i);
                    continue;
                }
                p.x += p.vx * dt;
                p.y += p.vy * dt;
                double age = p.life0 - p.life;
                double op = age < 0.5 ? (age / 0.5) * 0.65 : Math.Min(0.65, p.life / 1.2 * 0.65);
                Canvas.SetLeft(p.shape, p.x);
                Canvas.SetTop(p.shape, p.y);
                p.shape.Opacity = Math.Max(0, op);
            }
        }

        class Particle
        {
            public double x, y, vx, vy, life, life0;
            public FrameworkElement shape;   // 气泡/灰尘用 Ellipse，Zzz 用 TextBlock
        }

        void Bounce()
        {
            bounceUntil = DateTime.Now.AddMilliseconds(560);
            DoubleAnimationUsingKeyFrames kf = new DoubleAnimationUsingKeyFrames();
            kf.Duration = TimeSpan.FromMilliseconds(520);
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(0.97, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
            kf.Completed += delegate { tfScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); };
            tfScale.BeginAnimation(ScaleTransform.ScaleXProperty, kf);
            DoubleAnimationUsingKeyFrames jump = new DoubleAnimationUsingKeyFrames();
            jump.Duration = TimeSpan.FromMilliseconds(520);
            jump.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            jump.KeyFrames.Add(new LinearDoubleKeyFrame(-16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            jump.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
            // 蹦完把动画摘掉，否则它会一直按住属性、待机定时器就写不进去了
            jump.Completed += delegate { tfTranslate.BeginAnimation(TranslateTransform.YProperty, null); };
            tfTranslate.BeginAnimation(TranslateTransform.YProperty, jump);
            suspendBlinkUntil = DateTime.Now.AddMilliseconds(700);
        }

        // 交叉淡入切换表情
        void SetMood(string mood, bool immediate)
        {
            // 姿态层占着立绘时，站立表情系统让位（姿态图自带表情）—— 但**仍要记住 mood**：
            // 否则姿态切回 idle 的那一帧会先按旧表情画图（困倦立绘）再切普通立绘，
            // 看起来就是"困倦与待机来回切"（用户报的 bug）。
            if (pose != "idle") { currentMood = mood; return; }
            if (mood == currentMood) return;
            if (!moods.ContainsKey(mood)) mood = "idle";
            if (!moods.ContainsKey(mood)) return;
            PetConfig.Log("mood=" + mood + " (was " + currentMood + ")");   // 表情审计：排查"表情来回切"
            BitmapImage next = moods[mood];
            if (immediate)
            {
                imgBase.BeginAnimation(UIElement.OpacityProperty, null);
                imgBase.Opacity = 1;
                imgBase.Source = next;
                imgFade.BeginAnimation(UIElement.OpacityProperty, null);
                imgFade.Opacity = 0;
                currentMood = mood;
                return;
            }
            imgFade.Source = next;
            string target = mood;
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320));
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
            fadeIn.Completed += delegate
            {
                // 只有在这次淡入仍是最新目标时才落地，避免连续切换时错帧
                if (currentMood != target) return;
                imgBase.BeginAnimation(UIElement.OpacityProperty, null);
                imgBase.Source = next;
                imgBase.Opacity = 1;
                imgFade.BeginAnimation(UIElement.OpacityProperty, null);
                imgFade.Opacity = 0;
            };
            imgFade.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            imgBase.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            currentMood = mood;
        }

        void ApplyMood(bool animate)
        {
            // 即时表情保持期（点击＝比耶、充值＝开心等）：这段时间内不许被余额/场景刷回去，
            // 否则就会出现"点一下她的表情只闪了一瞬间"（用户报的 bug）
            if (DateTime.Now < moodHoldUntil) return;
            PetSnapshot s;
            lock (stateLock) { s = latest; }
            string mood = cfg.moodOverride;
            if (mood == "auto" || string.IsNullOrEmpty(mood))
            {
                if (s != null && s.ok && (!s.isAvailable || (!double.IsNaN(s.total) && s.total < s.lowBalance))) mood = "worry";
                else if (scene == "doze") mood = "sleepy";
                else if (scene == "interrupted") mood = "worry";
                else mood = "idle";
            }
            SetMood(mood, !animate);
        }

        // ================= 说话 =================
        /// <summary>
        /// 余额播报这类"有信息量但不紧急"的台词：忙时**排队等空闲**（不丢弃），队内也**不过期**。
        /// （用户反馈：按"次要台词丢弃"处理会把余额播报吞掉）
        /// </summary>
        void SaySoft(string text, double seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (sayBusy) { sayQueue.Enqueue(new object[] { text, seconds, DateTime.Now, true }); PetConfig.Log("say(soft-queued): " + text); return; }
            Say(text, seconds);
        }
        /// <summary>关键台词（状态切换/干活/拎起）：立刻打断当前台词并清空队列（A 方案）</summary>
        /// <summary>
        /// 显隐改成"淡入/淡出"（用户诊断 A 类：切换瞬间的突变）。
        /// 只动 Opacity + 在淡出结束后才 Hidden —— 尺寸与位置完全不变，因此不会产生布局瞬变。
        /// </summary>
        void FadeVis(FrameworkElement el, bool show, int ms)
        {
            if (el == null) return;
            try
            {
                if (show)
                {
                    el.Visibility = Visibility.Visible;
                    el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(el.Opacity, 1.0, TimeSpan.FromMilliseconds(ms)));
                }
                else
                {
                    DoubleAnimation fade = new DoubleAnimation(el.Opacity <= 0.05 ? 1.0 : el.Opacity, 0.0, TimeSpan.FromMilliseconds(ms));
                    fade.Completed += delegate { el.Visibility = Visibility.Hidden; el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; };
                    el.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }
            catch (Exception ex) { PetConfig.Log("fadevis failed: " + ex.Message); }
        }

        /// <summary>
        /// 立即刷新贴纸的忙碌/游戏标签（不等轮询）—— 用户反馈：贴纸滞后于状态很多。
        /// 场景一变就调它，标签立刻跟上；离线的处理仍交给轮询路径。
        /// </summary>
        void RefreshChipLabel()
        {
            try
            {
                if (chipIcon == null) return;
                PetSnapshot s = null;
                lock (stateLock) { s = latest; }
                if (s == null || !s.ok) return;
                if (!s.isAvailable || (!double.IsNaN(s.total) && s.total < s.lowBalance)) return;
                string t = s.busy ? "深度思考中" : (scene == "gaming" ? "深度游戏中" : "");
                if (chipIcon.Text != t)
                {
                    chipIcon.Text = t;
                    PetConfig.Log("chip label = [" + t + "] (scene=" + scene + ")");
                }
            }
            catch (Exception ex) { PetConfig.Log("chip label refresh failed: " + ex.Message); }
        }

        void SayKey(string text, double seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            saySeq++;                 // 让正在进行的淡出回调失效
            sayQueue.Clear();
            sayBusy = false;
            Say(text, seconds);
        }

        void Say(string text, double seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            // 次要台词（点击/播报/换游戏）忙时直接丢弃，不排队 —— 避免积压导致台词滞后于状态（A 方案）
            if (sayBusy) { PetConfig.Log("say(dropped): " + text); return; }
            sayBusy = true;
            PetConfig.Log("say: " + text);   // 台词审计：确认每次状态切换说的话是否正确、有没有多余台词
            bubbleText.Text = text;
            bubble.Visibility = Visibility.Visible;
            bubble.BeginAnimation(UIElement.OpacityProperty, null);
            bubble.Opacity = 1;
            int mySeq = ++saySeq;                 // 序号守卫：只有"最新那条台词"才允许把自己藏起来
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fade.BeginTime = TimeSpan.FromSeconds(Math.Max(2.5, seconds));   // 最短显示 2.5s，避免"一闪就没"
            fade.Completed += delegate
            {
                if (mySeq != saySeq) return;      // 已被更新的台词取代 → 不要隐藏新气泡
                bubble.Visibility = Visibility.Hidden; bubble.Opacity = 1;
                sayBusy = false;
                if (sayQueue.Count > 0)   // 排队中的下一句（用户要求：多句依次播完，不互相覆盖）
                {
                    object[] nx = sayQueue.Dequeue();
                    sayBusy = false;
                    Say(Convert.ToString(nx[0]), Convert.ToDouble(nx[1]));
                }
            };
            bubble.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ================= 余额轮询 =================
        void StartPolling()
        {
            pollTimer = new DispatcherTimer();
            pollTimer.Interval = TimeSpan.FromSeconds(1.2);
            pollTimer.Tick += delegate { TickPoll(); };
            pollTimer.Start();
            pollThread = new Thread(PollLoop);
            pollThread.IsBackground = true;
            pollThread.Start();
        }

        void TickPoll()
        {
            // 长轮询模式下由 PollLoop 独占驱动，这里只做场景与表情的推进
            if (cfg.longPollSeconds <= 0)
            {
                bool needRefresh = (DateTime.Now - lastPoll).TotalSeconds > Math.Max(2, cfg.pollSeconds);
                if (needRefresh) RefreshNow(false);
            }
            UpdateScene();
            ApplyMood(false);
        }

        /// <summary>
        /// 每帧（50ms）视觉校正：① 投影光柱跟随姿态与窗口尺寸；② 姿态优先不变量。
        /// 注：原每帧更新里「探头模式键鼠高亮 + 补丁帧」的部分已随书桌场景一并移除。
        /// </summary>
        void UpdateBeamAndPose()
        {
            UpdateBeam();   // 投影光柱跟着姿态与窗口尺寸走
            // 姿态优先不变量（v0.2.0）：只要处于非 idle 姿态，imgBase 就必须显示姿态图。
            // 表情系统/眨眼层/场景机里任何一处回写都会被这里在 50ms 内纠正回来 —— 上一轮
            // "姿态切了但实机还站着"就是这个不变量缺失导致的。
            if (!fadingThrough && pose != "idle" && poses.ContainsKey(pose) && imgBase != null
                && !ReferenceEquals(imgBase.Source, PoseSpriteFor(pose)))   // 注意用 PoseSpriteFor：被打断时是"惊讶"变体
            {
                BitmapImage wantPose = PoseSpriteFor(pose);
                imgBase.BeginAnimation(UIElement.OpacityProperty, null);
                imgBase.Opacity = 1;
                imgBase.Source = wantPose;
                if (imgFade != null)
                {
                    imgFade.BeginAnimation(UIElement.OpacityProperty, null);
                    imgFade.Opacity = 0; imgFade.Source = null;
                }
                if (imgBlink != null)
                {
                    imgBlink.BeginAnimation(UIElement.OpacityProperty, null);
                    imgBlink.Opacity = 0; imgBlink.Source = null;
                }
                PetConfig.Log("pose re-asserted: " + pose);
            }
        }

        void ApplyLayout()
        {
            if (cfg.screenOnly)
            {
                // 挂机模式：窗口只装得下游戏屏 + 气泡（她已收起）
                win.Width = ScreenW + 28;
                win.Height = (screenVisible ? ScreenH + ScreenChrome : 0) + BubbleRow + 40;
                return;
            }
            double w = cfg.spriteWidth + 28;
            double h = cfg.spriteWidth * 1.52 + BubbleRow + 40;
            // 屏幕空间**始终预留**：场景切换不再改变窗口尺寸 → 无布局瞬变帧
                w = Math.Max(w, ScreenW + 28);
                h += ScreenH + ScreenChrome;
            win.Width = w;
            win.Height = h;
        }

        double RowActual(int i)
        {
            if (root == null || i >= root.RowDefinitions.Count) return 0;
            return ((RowDefinition)root.RowDefinitions[i]).ActualHeight;
        }

        // ================= 场景机（待机 / 玩游戏 / 趴睡 / 干活）=================
        void UpdateScene()
        {
            // ⚠ 这里**不能整段冻结**：手动锁定姿态时若直接 return，连"待机/打瞌睡"的自动计时也会一起停
            // （用户报的"自动变换场景只剩打游戏转干活"）。正确做法＝场景机照常跑，只把"姿态"与"屏幕"
            // 两处按锁定状态做精准抑制：姿态由 SetPose 的守卫拦下，屏幕在 SetScene 里按锁定姿态判定。
            string next;
            if (DateTime.Now < devHoldUntil) return;   // 开发者刚"置于某状态"：先冻结场景机，便于观察；点击或 20s 后自动恢复
            // ⚠ 拖动期间冻结场景机：她正被拎在手里，场景不该擅自切换 ——
            // 否则会出现"拎着拎着场景变成 gaming，松手后立绘从悬空的 pick 变成坐姿 handheld"
            // （用户报的"拎起时角色突然变小 + 锁进游戏状态"就是这个）
            if (dragging) return;
            // 「干活 / 被打断」优先级最高：正在被叫去干活时，不能被 screenMode=on 之类的规则踩回去
            if (scene == "work" || scene == "interrupted")
            {
                next = DateTime.Now < busyUntil ? scene : "idle";
            }
            else if (manualScene != null) next = manualScene;   // 手动切过姿态：场景跟随手动值，不被常开规则推回 gaming
            // 刚被点醒的"迷糊缓冲"：放在这里（而不是自动分支里），这样**常开模式下也生效**；
            // 只在她仍处于打瞌睡时保持，避免 doze⇄idle 反复横跳（用户报的 bug）
            else if (scene == "doze" && DateTime.Now < dozeWakeUntil) next = "doze";
            else if (dragIdleUntil != DateTime.MinValue && DateTime.Now < dragIdleUntil) next = "idle";   // 预留开关
            else if (cfg.screenMode == "on") next = "gaming";
            else if (cfg.screenMode == "off") next = "idle";
            else
            {
                double idle = (DateTime.Now - lastUserAction).TotalSeconds;        // 任意互动（含悬停/拖动/右键）
                double sceneIdle = (DateTime.Now - lastSceneReset).TotalSeconds;   // 只有单击才重置
                if (devAutoScene != null) next = devAutoScene;                     // （已废弃的长期覆盖，保留兼容）
                // （"刚被点醒的迷糊缓冲"已上移到场景机开头，此处不再重复判断）
                else if (idle >= cfg.sleepIdleSeconds) next = "doze";
                else if (sceneIdle >= cfg.gameIdleSeconds) next = "gaming";
                else next = "idle";
            }
            if (next != scene) SetScene(next);
        }

        void SetScene(string next)
        {
            string prev = scene;
            scene = next;
            if (next == prev) return;   // 场景未变：直接返回，避免重复播开机/关屏动画（用户报的"闪两次"）
            // 场景轨迹写日志：这是排查「被叫去干活 → 关掉游戏」这类链路的主要依据
            PetConfig.Log("scene=" + next + " screenVisible=" + screenVisible + (next == prev ? " (same)" : ""));
            RefreshChipLabel();   // 场景变了→贴纸标签立即跟上（不等轮询）
            // 屏幕显隐：**必须尊重菜单里的"游戏屏"三档**（自动/常开/关闭）——
// 之前我把这里简化成"只看场景"，导致菜单那三项点了没反应（用户报的 bug）。
                bool wantScreen = cfg.screenMode == "on" ? true
                                : cfg.screenMode == "off" ? false
                                : (cfg.screenOnly || next == "gaming" || next == "interrupted");
            if (next != "doze" && pose != "doze") moodHoldUntil = DateTime.MinValue;   // 离开打瞌睡就立刻恢复常规表情，避免残留
            // 手动锁定为"非掌机"姿态时不弹游戏屏（例如锁定"站着待机"却让场景机跑成 gaming）
            if (PetApp.PoseTest.Length > 0 && PetApp.PoseTest != "handheld") wantScreen = false;
            if (wantScreen != screenVisible)
            {
                // 只切屏幕显隐：**窗口尺寸/位置一律不动**（屏幕空间在 ApplyLayout 里始终预留），
                // 所以这里不存在"布局瞬变帧" —— 立绘既不会被裁、也不需要隐藏（隐藏会让整个人闪一下）。
                // 于是"震惊 → 冥想"这段过渡可以完整演出来（这正是用户提示的：把改动塞进已有过渡里）。
                screenVisible = wantScreen;
                FadeVis(screenBezel, wantScreen, 280);   // 淡入/淡出，而不是瞬间显隐（A 类突变）
                if (wantScreen && mc != null) mc.Reset();
            }
            if (next == "gaming" && prev != "gaming")
            {
                // 进入游戏：**用户手动选定过就固定用那款**，否则随机挑一款（用户要求）
                if (!cfg.gameFixed)
                {
                    string[] pool = new string[] { "fp", "side", "vn" };
                    cfg.game = pool[rnd.Next(pool.Length)];
                    SwitchScreen();
                    PetConfig.Log("gaming: 随机进入游戏画面 = " + cfg.game + "（未手动选定）");
                }
                else
                {
                    PetConfig.Log("gaming: 用固定的游戏画面 = " + cfg.game);
                }
                SayKey(GamingEntryLine(), 5);
            }
            else if (next == "doze" && prev != "doze") Say("唔……困了，趴一会儿……", 5);
            // 「被叫去干活」在台词上＝"深度求索"（用户要求：冥想状态就该说深度求索的话，不能是游戏台词）
            else if (next == "work" && prev != "work")
            {
                // （叹气落座已移除，只保留渐变过渡）
                nextFadeMs = 600;                                  // 溶解拉长到 600ms（默认 320ms）
                SayKey(rnd.Next(2) == 0 ? "进入深度求索…让我安静想想" : "正在深度求索，稍等一下～", 4.5);
            }
            ApplyMood(false);
            SetPose(PoseForScene(), true);     // 姿态跟场景走：gaming=掌机 / doze=打瞌睡 / work=冥想
        }

        /// <summary>宿主开始干活：正在玩游戏 → 统一演出「惊一下 + 关掉游戏画面 + 关掉悬浮屏」</summary>
        void OnWorkStart()
        {
            busyUntil = DateTime.Now.AddMinutes(3);
            // 常开模式（screenMode=on）下用户要求：进入冥想＝"正在深度求索"，**游戏屏直接关掉**，
            // 不走"惊一下 → 暂停条"那套演出（屏幕一直开着时那套显得啰嗦）。
            // 常开模式的"直接关屏"捷径已删除（用户实测：那样既没有暂停条、也没有惊讶脸）。
            // 现在无论哪种游戏屏模式，被叫去干活都统一走"惊一下 → 暂停条 → 关屏 → 冥想"。
            // 「暂停条演出」的触发条件（用户要求收紧）：必须是**掌机模式**（她正在玩：姿态 handheld 或
            // 场景 gaming）且**屏幕正显示**，并且只能由"用户指令开始干活"这条链路进入（本方法只被工作事件调用）。
            // 其它情况一律直接 SetScene("work")，不播暂停演出。
            if (screenVisible && (pose == "handheld" || scene == "gaming"))
            {
                Bounce();
                SetMood("worry", false);
                suspendBlinkUntil = DateTime.Now.AddSeconds(2.5);
                SayKey("呜哇！？主人要干活了……！", 3);
                scene = "interrupted";
                PetConfig.Log("scene=interrupted (被叫去干活)");   // 这条原来直接改字段、没记日志，排查时看不到
                if (mc != null) mc.Panic();      // 各屏演自己的"惊一下"（MC = 苦力怕探头）
                workPhase = 1; workT = 0;        // 收尾统一走 StepWork：暂停条 → 收屏 → 干活
            }
            else
            {
                SetScene("work");
            }
        }

        /// <summary>
        /// 「被叫去干活」的统一收尾（v0.0.9）：
        /// 阶段 1 各屏先惊一下（MC 屏等它的苦力怕梗演完，其它屏给 1.2 秒）；
        /// 阶段 2 统一播「游戏暂停条 → 白闪抖动 → 竖向收屏 → 亮线熄灭」；
        /// 结束后收起悬浮屏、换成困倦脸、说一句不情愿的话。
        /// 在这之前只有 MC 屏有反应，另外两块屏是"直接消失"，观感不统一。
        /// </summary>
        void StepWork(double dt)
        {
            if (workPhase == 0) return;
            workT += dt;
            if (workPhase == 1)
            {
                // v0.0.10：3D 屏不再有死亡演出，苦力怕只探头（约 2.15s），所以改成定时收尾
                double need = (mc is McScreen) ? 1.8 : 1.2;
                if (workT > need) { workPhase = 2; workT = 0; }
                return;
            }
            if (workPhase == 2)
            {
                StepCloseScreen(workT);
                if (workT >= 0.95) FinishCloseScreen();
            }
        }

        /// <summary>关屏动画（约 0.95 秒，全部代码驱动，不留常驻动画）</summary>
        void StepCloseScreen(double t)
        {
            double flash = t < 0.16 ? (1 - t / 0.16) * 0.85 : 0;
            if (screenFlash != null) screenFlash.Opacity = flash;
            double shake = (t < 0.5) ? (((int)(t * 40) % 2 == 0) ? 2 : -2) : 0;
            if (screenShake != null) screenShake.X = shake;
            if (screenPause != null)
            {
                double p = (t - 0.06) / 0.39;
                if (p < 0) p = 0; if (p > 1) p = 1;
                screenPause.Margin = new Thickness(0, -30 + 30 * Math.Min(1, p * 1.6), 0, 0);
                screenPause.Opacity = 1 - Math.Max(0, (t - 0.48) / 0.18);
            }
            if (screenScale != null)
            {
                double s = t > 0.45 ? Math.Max(0.02, 1.0 - (t - 0.45) / 0.27) : 1.0;
                screenScale.ScaleY = s;
                screenScale.ScaleX = t > 0.6 ? Math.Max(0.98, 1.0 - (t - 0.6) * 0.05) : 1.0;
            }
            if (screenLine != null)
            {
                double o = t > 0.45 ? Math.Min(1, (t - 0.45) / 0.1) * Math.Max(0, 1 - (t - 0.72) / 0.23) : 0;
                screenLine.Opacity = o;
            }
        }

        void FinishCloseScreen()
        {
            workPhase = 0; workT = 0;
            if (screenScale != null) { screenScale.ScaleY = 1; screenScale.ScaleX = 1; }
            if (screenShake != null) screenShake.X = 0;
            if (screenFlash != null) screenFlash.Opacity = 0;
            if (screenLine != null) screenLine.Opacity = 0;
            if (screenPause != null) { screenPause.Opacity = 0; screenPause.Margin = new Thickness(0, -30, 0, 0); }
            SetScene("work");
            SetMood("sleepy", false);                     // 不情愿的那个表情（沿用既有困倦立绘）
            Say(rnd.Next(2) == 0 ? "唔…那就认真干活吧。" : "好啦好啦，我去忙了～", 4);   // 原"……好吧，我去干活了。"时序反了（此时她已在干活）
        }

        void RefreshNow(bool announce)
        {
            lastPoll = DateTime.Now;
            try { pollTimer.Stop(); pollTimer.Start(); } catch { }
            ThreadPool.QueueUserWorkItem(delegate { DoFetch(announce); });
        }

        void PollLoop()
        {
            while (running)
            {
                Thread.Sleep(1000);
                if ((DateTime.Now - lastPoll).TotalSeconds > Math.Max(2, cfg.pollSeconds))
                {
                    lastPoll = DateTime.Now;
                    DateTime t0 = DateTime.Now;
                    DoFetch(false);
                    // 服务端不支持长轮询（旧版插件 / 别的服务）时会立刻返回：
                    // 这时退化成普通轮询节奏，避免 1 秒一次的空转。
                    if ((DateTime.Now - t0).TotalSeconds < 1.5) Thread.Sleep(Math.Max(1, cfg.pollSeconds) * 1000);
                }
            }
        }

        void DoFetch(bool announce)
        {
            PetSnapshot snap = Fetch();
            win.Dispatcher.BeginInvoke(new Action(delegate { OnState(snap, announce); }));
        }

        PetSnapshot Fetch()
        {
            PetSnapshot s = new PetSnapshot();
            try
            {
                // 带上自己的 pid，插件据此判断桌宠是否活着（并能在需要时关掉它）
                string url = cfg.stateUrl;
                int myPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                url += (url.IndexOf('?') >= 0 ? "&" : "?") + "pid=" + myPid;
                if (cfg.longPollSeconds > 0) url += "&wait=" + cfg.longPollSeconds;
                if (DebugMode) PetConfig.Log("[debug] GET " + url);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = null;
                int ms = cfg.longPollSeconds > 0 ? (cfg.longPollSeconds + 12) * 1000 : 4000;
                req.Timeout = ms;
                req.ReadWriteTimeout = ms;
                req.Method = "GET";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    if (!string.IsNullOrEmpty(json)) File.WriteAllText(PetConfig.PathCache, json, Encoding.UTF8);
                    s = ParseState(json);
                    s.ok = true;
                }
            }
            catch (Exception ex)
            {
                PetConfig.Log("fetch failed: " + ex.Message);
                try
                {
                    if (File.Exists(PetConfig.PathCache))
                    {
                        s = ParseState(File.ReadAllText(PetConfig.PathCache, Encoding.UTF8));
                        s.ok = false;   // 数据是缓存，标记为离线
                    }
                }
                catch { }
            }
            return s;
        }

        PetSnapshot ParseState(string json)
        {
            PetSnapshot s = new PetSnapshot();
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(json);
            if (d == null) return s;
            object balObj;
            if (d.TryGetValue("balance", out balObj) && balObj is Dictionary<string, object>)
            {
                Dictionary<string, object> b = (Dictionary<string, object>)balObj;
                s.currency = GetS(b, "currency", "CNY");
                s.total = GetD(b, "total", double.NaN);
                s.granted = GetD(b, "granted", 0);
                s.toppedUp = GetD(b, "toppedUp", 0);
                s.isAvailable = GetB(b, "isAvailable", true);
            }
            s.message = GetS(d, "message", "");
            s.command = GetS(d, "command", "");
            s.activityLabel = GetS(d, "activityLabel", "");
            s.busy = GetB(d, "busy", false);
            s.updatedAt = GetS(d, "updatedAt", "");
            object ev;
            if (d.TryGetValue("event", out ev) && ev is Dictionary<string, object>)
            {
                Dictionary<string, object> e = (Dictionary<string, object>)ev;
                s.eventSeq = (long)GetD(e, "seq", -1);
                s.eventKind = GetS(e, "kind", "");
            }
            s.lowBalance = GetD(d, "lowBalance", cfg.lowBalance);
            return s;
        }

        static string GetS(Dictionary<string, object> d, string k, string def)
        { object v; return (d.TryGetValue(k, out v) && v != null) ? Convert.ToString(v) : def; }
        static double GetD(Dictionary<string, object> d, string k, double def)
        { object v; if (d.TryGetValue(k, out v) && v != null) { double r; if (double.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out r)) return r; } return def; }
        static bool GetB(Dictionary<string, object> d, string k, bool def)
        { object v; if (d.TryGetValue(k, out v) && v != null) { bool r; if (bool.TryParse(Convert.ToString(v), out r)) return r; } return def; }

        void OnState(PetSnapshot s, bool announce)
        {
            // 插件点名让桌宠退出（右键菜单里没有「被远端关掉」的入口，这是唯一的软关闭通道）
            if (!string.IsNullOrEmpty(s.command) && s.command == "quit")
            {
                PetConfig.Log("received quit command from plugin");
                cfg.Save();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                running = false;
                win.Close();
                return;
            }
            PetSnapshot prev;
            lock (stateLock) { prev = latest; latest = s; }
            bool wasOk = prev != null && prev.ok;
            if (s.ok) failCount = 0; else failCount++;
            // 连续拿不到余额 → 说明一次（只在跨过阈值那一刻说，成功后 failCount 归零会重新计数）
            if (failCount == 4)
            {
                PetConfig.Log("balance: 连续 " + failCount + " 次拿不到余额 → 提示用户");
                SaySoft("咦…我暂时看不到余额了…（拿不到数据）", 5);
            }

            // 余额徽章
            chipText.Text = "余额 " + s.TotalText;
            if (!s.ok)
            {
                chipDot.Fill = new SolidColorBrush(Color.FromArgb(255, 150, 150, 160));
                chipIcon.Text = "离线";
                chip.Background = new SolidColorBrush(Color.FromArgb(225, 48, 52, 66));
                chip.Opacity = 0.9;
            }
            else if (!s.isAvailable || (!double.IsNaN(s.total) && s.total < s.lowBalance))
            {
                chipDot.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 140, 120));
                chipIcon.Text = s.isAvailable ? "偏低" : "不可用";
                chip.Background = new SolidColorBrush(Color.FromArgb(238, 92, 32, 40));
                chip.Opacity = 1;
            }
            else
            {
                chipDot.Fill = new SolidColorBrush(Color.FromArgb(255, 96, 220, 140));
                // 忙碌标记：冥想（work）＝"正在深度求索"；游戏屏开着时的忙碌＝"深度游戏中"
                // 忙碌标签按 **DSH 实际忙碌状态** 判断（不是按场景）：
                // 原先写 scene == "work"，但"被打断 → 正在求索"期间场景可能还是 interrupted，
                // 于是掉进 else 显示"深度游戏中"（用户实测：冥想时贴纸仍写"深度游戏中"）。
                // 文案用"深度思考"（与 DSH 的功能名呼应，也更准确 —— 她不是只在玩游戏）。
                // 只按 scene 判断（原来还要求 screenVisible，那个标记会残留 → 待机时仍显示"深度游戏中"，用户实测）
                chipIcon.Text = s.busy ? "深度思考中" : (scene == "gaming" ? "深度游戏中" : "");
                chip.Background = new SolidColorBrush(Color.FromArgb(235, 26, 40, 78));
                chip.Opacity = 1;
            }

            // 金额变化播报
            // ⚠ 必须去重 + 冷却：这个分支曾反复触发（不同调用路径下 prev 未推进 → 每轮都判成"余额上涨"），
            // 表现就是她在"比耶(happy)"与"站立(idle)"之间反复闪 —— 用户报的闪烁 bug。
            bool changed = s.ok && wasOk && !double.IsNaN(s.total) && !double.IsNaN(prev.total)
                           && Math.Abs(s.total - prev.total) >= 0.01
                           && (double.IsNaN(lastAnnouncedTotal) || Math.Abs(s.total - lastAnnouncedTotal) >= 0.01)
                           && DateTime.Now >= balanceAnnounceUntil;
            if (changed)
            {
                double delta = s.total - prev.total;
                lastAnnouncedTotal = s.total;
                balanceAnnounceUntil = DateTime.Now.AddSeconds(6);   // 20 秒冷却
                if (delta > 0)
                {
                    SaySoft("收到充值 " + s.TotalText + "，谢谢主人！" + PorkLine(s.total) + " " + BalanceComment(s.total), 6);
                    suspendBlinkUntil = DateTime.Now.AddSeconds(2.2);
                }
                else
                {
                    SaySoft("刚刚花掉了 " + Math.Abs(delta).ToString("0.00") + "，还剩 " + s.TotalText
                         + PorkLine(s.total) + " —— " + BalanceComment(s.total), 6);
                    // 表情跟随"播报时的余额档位"：< ¥16（2 份红烧肉）临时换成担心脸，
                    // 但**只在播报期间**；气泡消失（6s + 0.4s 淡出）之后约 1 秒回到普通表情。
                    if (!double.IsNaN(s.total) && s.total < PorkPrice * 2)
                    {
                        SetMood("worry", false);
                        moodHoldUntil = DateTime.Now.AddSeconds(6 + 0.4 + 1.0);
                    }
                }
            }
            if (s.ok && !string.IsNullOrEmpty(s.message) && s.message != lastMessage)
            {
                lastMessage = s.message;
                Say(s.message, 8);
            }
            if (announce) Say(s.ok ? ("当前余额 " + s.TotalText + "（" + s.currency + "）") : "连不上 DSH 插件，稍后再试～", 5);

            // 宿主事件：开始干活 → 正在玩游戏就被吓一跳（R2d）
            if (s.eventSeq > lastEventSeq)
            {
                lastEventSeq = s.eventSeq;
                if (s.eventKind == "work-start") OnWorkStart();
                else if (s.eventKind == "work-end")
                {
                    busyUntil = DateTime.MinValue;
                    if (scene == "work" || scene == "interrupted") SetScene("idle");
                }
            }
            if (!s.busy && scene == "work")
            {
                busyUntil = DateTime.MinValue;
                SetScene("idle");
            }

            ApplyMood(false);
        }

        // ================= 鼠标交互 =================
        // DragMove() 会一直阻塞到松开左键，因此用它同时完成「拖动」与「点击」的判定：
        // 松手后鼠标在屏幕上几乎没动 = 单击，动了 = 拖动过（只保存位置，不触发互动）。
        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            lastUserAction = DateTime.Now;
            System.Drawing.Point p0 = WinForms.Control.MousePosition;
            dragging = true;                              // 拖着的时候：不玩游戏、不出光、不出屏
            pickShown = false;
            // 长按 180ms 才进入"被拎起"：单击（想触发对话/互动）不会闪出拎起动作
            if (pickTimer == null)
            {
                pickTimer = new DispatcherTimer();
                pickTimer.Interval = TimeSpan.FromMilliseconds(180);
                pickTimer.Tick += delegate
                {
                    pickTimer.Stop();
                    if (!dragging || pickShown) return;
                    if (WinForms.Control.MouseButtons != WinForms.MouseButtons.Left) return;  // 已松手＝单击
                    // 用户要求（v0.2.2）："被拎起"只在**站立待机**姿态下触发。
                    // 其它姿态（拿掌机 / 打瞌睡 / 冥想）都是坐姿，被拎起来很怪 ——
                    // 那些状态下拖动＝单纯搬动她：不改姿态、不动屏幕与光柱。
                    // 用户要求：**已经在拎起状态时再被拎一次，也要说拎起台词**——
                    // 所以 pick 本身不算"非站立"，要放行到下面的台词分支（SetPose 同姿态是空操作，不影响）
                    if (pose != "idle" && pose != "pick")
                    {
                        PetConfig.Log("drag hold: pose=" + pose + " 非站立 → 保持姿态，仅搬动");
                        return;
                    }
                    pickShown = true;
                    SetPose("pick", false);
                    SayKey(PickLines[rnd.Next(PickLines.Length)], 3);   // 被拎起来的抱怨台词（用户要求）
                    HideBeam();
                    // ⚠ 一律用 Opacity，不能 Collapsed：屏幕在 Auto 行，塌行会把立绘顶上去 180 DIP
                    if (screenBezel != null) screenBezel.Opacity = 0;
                    PetConfig.Log("drag hold 180ms -> pick pose");
                };
            }
            pickTimer.Stop();
            pickTimer.Start();
            PetConfig.Log("drag start (game off)");
            try { win.DragMove(); } catch { }
            System.Drawing.Point p1 = WinForms.Control.MousePosition;
            if (pickTimer != null) pickTimer.Stop();
            double topAfterDrag = win.Top, leftAfterDrag = win.Left;   // 记下松手时的真实位置
            cfg.x = win.Left; cfg.y = win.Top; cfg.Save();
            dragging = false;
            // 松手＝解除暂停，回到"她原本该在的状态"（用户要求：拎起暂停、放下继续）
            // 但**跳过过渡帧**（不想再闪一次"举机"），屏幕与光柱自行恢复
            skipTrans = true;
            string backPose = PetApp.PoseTest.Length > 0 ? PetApp.PoseTest : PoseForScene();  // 菜单锁定的姿态优先
            SetPose(backPose, true);
            skipTrans = false;
            // 恢复屏幕：必须按"当前是否该显示屏幕"来决定，不能无条件设 1 ——
            // 否则用户把状态切成待机/冥想/打瞌睡之后再拖一下她，游戏屏就被带回来了（用户报的 bug）
            bool wantScreenBack = PetApp.PoseTest.Length > 0 ? (PetApp.PoseTest == "handheld") : screenVisible;
            if (screenBezel != null) screenBezel.Opacity = wantScreenBack ? 1 : 0;
            // ⚠ 位置钉住：拖动期间场景机可能切状态（SetScene 会按 ScreenH+ScreenChrome 搬窗口），
            // 那就是用户看到的"点一下她就飞起来"。松手后把窗口放回鼠标松开的位置。
            win.Left = leftAfterDrag; win.Top = topAfterDrag;
            cfg.x = win.Left; cfg.y = win.Top; cfg.Save();
            PetConfig.Log("drag end -> resume pose=" + pose + " (no transition frame)");
            double dx = Math.Abs(p1.X - p0.X), dy = Math.Abs(p1.Y - p0.Y);
            if (dx < 4 && dy < 4)
            {
                OnPetted();     // 单击：蹦跳 + 星星 + 台词（抽成方法，供 --clicktest 复用）
            }
            else if (pickShown)
            {
                // 落地扬尘：只在**本次真的被拎起过**（pickShown，且需站立姿态 + 位移 ≥4px）时出现。
                // 非站立姿态（拿掌机/打瞌睡/冥想）不触发拎起 → 也不该有落地扬尘（用户指出的一致性）。
                // 拎起/放下的脚底扬尘已删除（用户反馈效果不好）
            }
        }

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            lastUserAction = DateTime.Now;
            if (menu != null)
            {
                menu.PlacementTarget = win;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.HorizontalOffset = 0;
                menu.VerticalOffset = 0;
                menu.IsOpen = true;
            }
            e.Handled = true;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            SetScale(cfg.spriteWidth + (e.Delta > 0 ? 18 : -18));
        }

        void SetScale(double w)
        {
            cfg.spriteWidth = Math.Max(120, Math.Min(460, w));
            ApplyLayout();
            Rect wa = SystemParameters.WorkArea;
            if (win.Left > wa.Right - 60) win.Left = wa.Right - win.Width - 20;
            if (win.Top > wa.Bottom - 60) win.Top = wa.Bottom - win.Height - 10;
            cfg.x = win.Left; cfg.y = win.Top;
            cfg.Save();
        }
    }
}
