// McSide —— 2D 横版清关屏（Minecraft 画风）
// 鲸鱼娘从左往右走，右侧随机刷出树 / 僵尸 / 骷髅 / 蜘蛛 / 苦力怕；
// 她会砍树（原木 +1）、挥剑清怪、被撞掉血，偶尔被苦力怕炸出 You Died → 复活。
// 依旧是纯代码像素画，零美术素材；HUD 与伪 3D 屏共用 McHud。
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekPet
{
    class McSide : IGameScreen
    {
        public const int W = 120, H = 80;
        const int GroundY = 52;          // 草地表面
        const double StartX = 60;        // 起点世界坐标（开局与死亡重载都用它）
        const double MaidScreenX = 34;   // 女主固定在屏幕上的横坐标
        const double WalkSpeed = 9;      // 女主前进速度（世界 px/s）

        // ---- 调色板 ----
        static readonly int HAIR = Px.C(0xFF3E5FA8), HAIR2 = Px.C(0xFF2A4278), SKIN = Px.C(0xFFF5D0AE),
            EYE = Px.C(0xFF22304E), NAVY = Px.C(0xFF2B3A66), WHITE = Px.C(0xFFF2F4F8),
            SHOE = Px.C(0xFF20283C), TAIL = Px.C(0xFF6FA8D8), BLUSH = Px.C(0xFFE79A9A),
            GRASS = Px.C(0xFF57A83A), GRASS_D = Px.C(0xFF3F8A2A), DIRT = Px.C(0xFF7A5230),
            DIRT_D = Px.C(0xFF5E3D22), STONE = Px.C(0xFF8A8A8A), BARK = Px.C(0xFF6B4A22),
            BARK_D = Px.C(0xFF53380F), LEAF = Px.C(0xFF46A02C), LEAF_D = Px.C(0xFF2F7A22), LEAF_L = Px.C(0xFF63B93F);

        readonly WriteableBitmap bmp;
        readonly int[] buf = new int[W * H];
        readonly Random rnd = new Random();
        readonly McHud hud = new McHud();

        // ---- 状态机 ----
        enum Phase { Play, Died, Wake }
        enum Act { Walk, Chop, Attack, Hurt, Shoot, Eat }
        Phase phase = Phase.Play;
        double t;
        Act act = Act.Walk;
        double actT;
        int maidFrame;
        int hearts = 7;
        double hurtFlash;
        double iFrame;                   // 受击后的无敌时间（MC 的受伤硬直）
        double shootCd;                  // ds 娘射箭冷却
        double eatCd;                    // 吃东西冷却
        int food;                        // 身上带着的食物（怪物掉落）
        public int Logs { get; set; }    // 砍到的原木
        public double CreeperChance { get; set; }

        // 被"叫去干活"吓一跳时给桌宠看的标志：死亡/复活期间为 false
        public bool ShowDiedOverlay { get { return phase == Phase.Died; } }
        public ImageSource Source { get { return bmp; } }
        public string OverlayText { get { return ""; } }
        public string[] OverlayChoices { get { return emptyChoices; } }
        public string CharacterAsset { get { return ""; } }
        public string MoodAsset { get { return ""; } }
        public string BackgroundAsset { get { return ""; } }
        public bool Click(double nx, double ny) { return false; }
        /// <summary>双击场景区：从右边刷一只苦力怕走进来（保留原有彩蛋）</summary>
        public void DoubleClick() { TriggerCreeper(); }
        // 她的输入：走路 = 按住 D；挥剑/射箭 = 按住鼠标左键
        public bool KeyD { get { return act == Act.Walk; } }
        public bool MouseLeft { get { return act == Act.Attack || act == Act.Shoot; } }
        public int ClickPulse { get { return 0; } }
        static readonly string[] emptyChoices = new string[0];

        /// <summary>被叫去干活：她当场僵住、冒汗、抖两下（世界暂停）——收尾关屏由桌宠统一演</summary>
        public void Panic()
        {
            panicT = 2.2;
            act = Act.Walk; actT = 0; walkPhase = 0;
        }

        // ---- 世界 ----
        double maidX = 60;               // 女主世界横坐标
        double camX;                     // 摄像机
        double walkPhase;                // 走路帧相位
        double flash;

        class Mob
        {
            public string kind;
            public double x, hp, speed, t;
            public double hurt;               // >0 = 受击闪红（MC 里怪物被打到也会变红）
            public string state = "walk";     // walk | aim（骷髅拉弓）| hiss | dying
        }
        class Tree { public double x; public int hits; public bool alive = true; public double fallT; }
        class Puff { public double x, y, vx, vy, life, life0; public int color; }
        /// <summary>箭矢：骷髅射向 ds 娘，ds 娘偶尔也回敬一箭</summary>
        class Arrow { public double x, y, vx, vy; public bool fromMaid; }

        readonly List<Mob> mobs = new List<Mob>();
        readonly List<Tree> trees = new List<Tree>();
        readonly List<Puff> puffs = new List<Puff>();
        readonly List<Arrow> arrows = new List<Arrow>();
        double nextMobT = 2.5, nextTreeT = 4;
        double panicT;                  // >0 = 被叫去干活，僵住发抖
        double creeperCd;               // 招苦力怕的冷却：防止双击连点刷出一群（3D 屏早有，这边原来漏了）

        public McSide()
        {
            bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
            Reset();
        }

        public void Reset()
        {
            phase = Phase.Play; t = 0; act = Act.Walk; actT = 0; hearts = 7; hurtFlash = 0;
            mobs.Clear(); trees.Clear(); puffs.Clear(); arrows.Clear();
            maidX = StartX; camX = maidX - MaidScreenX; nextMobT = 2.5; nextTreeT = 4; flash = 0;
            iFrame = 0; shootCd = 5; eatCd = 0; food = 0; creeperCd = 0;
            Render();
        }

        /// <summary>
        /// You Died 之后的「重新加载」（用户要求）：**除了场景之外全部复位**。
        /// 场景（天空 / 远山 / 灌木 / 云 / 地面）是程序化画的，天然保留；
        /// 其余全部回初始：她回起点、树与怪物清空、箭矢与粒子清掉、血量・食物・原木复原、
        /// 刷怪计时重置 —— 怪物重新从最右侧刷出来。
        /// </summary>
        void Respawn()
        {
            phase = Phase.Play; t = 0;
            maidX = StartX; camX = maidX - MaidScreenX;
            hearts = 7; food = 0; Logs = 0;
            iFrame = 0; shootCd = 5; eatCd = 0; creeperCd = 0; panicT = 0; hurtFlash = 0; flash = 0;
            act = Act.Walk; actT = 0; walkPhase = 0; maidFrame = 0;
            mobs.Clear(); trees.Clear(); arrows.Clear(); puffs.Clear();
            nextMobT = 2.5; nextTreeT = 4;      // 与开局一致：怪物重新从最右侧进场
            PetConfig.Log("side respawn: 全部资源已重置（她回起点 " + (int)StartX + "，树/怪/箭/食物/原木清零）");
        }

        /// <summary>开发用（--sidedeath）：立刻死亡，便于验收「死亡 → 重新加载」</summary>
        public void DevKill() { hearts = 0; Die(); }

        /// <summary>
        /// 双击场景区 / 被叫去干活：从右缘刷一只苦力怕走进来。
        /// v0.0.25 规则（用户定）：**生成后 0.5s 冷却**（不再等它消失），**同屏最多 5 只**——
        /// 有用户就是想召一群苦力怕，所以不做"场上有就不再刷"，只用上限兜住。
        /// </summary>
        public void TriggerCreeper()
        {
            if (creeperCd > 0)
            {
                PetConfig.Log("side creeper ignored (cd=" + creeperCd.ToString("F2") + ")");
                return;
            }
            int live = CountLiveCreepers();
            if (live >= 5)
            {
                PetConfig.Log("side creeper ignored (同屏已满 " + live + " 只)");
                return;
            }
            creeperCd = 0.5;
            Mob m = new Mob();
            m.kind = "creeper"; m.hp = 3; m.speed = 8;
            // 屏幕世界范围是 [maidX-MaidScreenX, maidX-MaidScreenX+W)。
            // v0.0.27 按用户要求：生成在**右边缘之外 2–12px**（走进来），而不是屏内 —— 屏内出现像"凭空冒在中间"。
            // 偏移只有十几像素（不是之前那次的 54px），以 8px/s 约 0.3–1.5 秒就走进画面，点下去仍有反馈。
            m.x = maidX + W - MaidScreenX + 2 + rnd.Next(0, 11);
            m.state = "walk";
            mobs.Add(m);
            PetConfig.Log("side creeper spawned at world " + (int)m.x + " （右缘 " + (int)(maidX + W - MaidScreenX) + "，同屏 " + (live + 1) + "/5）");
        }

        int CountLiveCreepers()
        {
            int n = 0;
            for (int i = 0; i < mobs.Count; i++)
                if (mobs[i].kind == "creeper" && mobs[i].state != "dying") n++;
            return n;
        }

        /// <summary>取一帧像素快照（供 --mcdump 分镜表用）</summary>
        public int[] Snapshot()
        {
            Render();
            int[] copy = new int[buf.Length];
            Array.Copy(buf, copy, buf.Length);
            return copy;
        }

        // ------------------------------------------------------------------ 推进
        public void Tick(double dt)
        {
            t += dt;
            if (flash > 0) flash = Math.Max(0, flash - dt * 5);
            if (hurtFlash > 0) hurtFlash = Math.Max(0, hurtFlash - dt);

            switch (phase)
            {
                case Phase.Died:
                    if (t >= 2.2) { phase = Phase.Wake; t = 0; }
                    break;
                case Phase.Wake:
                    if (t >= 0.6) Respawn();
                    break;
                default:
                    UpdatePlay(dt);
                    break;
            }
            StepPuffs(dt);
            Render();
        }

        void UpdatePlay(double dt)
        {
            // 被叫去干活：僵在原地发抖，世界暂停（不走路、不刷怪、不结算）
            if (panicT > 0)
            {
                panicT -= dt;
                StepPuffs(dt);
                camX = maidX - MaidScreenX;
                return;
            }
            actT += dt;
            if (iFrame > 0) iFrame = Math.Max(0, iFrame - dt);
            if (shootCd > 0) shootCd = Math.Max(0, shootCd - dt);
            if (eatCd > 0) eatCd = Math.Max(0, eatCd - dt);
            if (creeperCd > 0) creeperCd = Math.Max(0, creeperCd - dt);

            // 最近的目标（只关心前方 24px 内）
            Mob target = null; double bestD = 1e9;
            for (int i = 0; i < mobs.Count; i++)
            {
                Mob m = mobs[i];
                if (m.state == "dying") continue;
                double d = m.x - maidX;
                if (d > -8 && d < 24 && d < bestD) { bestD = d; target = m; }
            }
            // 中距离目标：ds 娘会拿弓射它
            Mob far = null; double farD = 1e9;
            for (int i = 0; i < mobs.Count; i++)
            {
                Mob m = mobs[i];
                if (m.state == "dying") continue;
                double d = m.x - maidX;
                if (d >= 34 && d < 96 && d < farD) { farD = d; far = m; }
            }
            // 附近有没有怪（决定能不能安心吃东西）
            bool danger = false;
            for (int i = 0; i < mobs.Count; i++)
            {
                if (mobs[i].state == "dying") continue;
                if (Math.Abs(mobs[i].x - maidX) < 40) { danger = true; break; }
            }
            Tree tree = null; double bestT = 1e9;
            for (int i = 0; i < trees.Count; i++)
            {
                Tree tr = trees[i];
                if (!tr.alive) continue;
                double d = tr.x - maidX;
                if (d > -4 && d < 20 && d < bestT) { bestT = d; tree = tr; }
            }

            switch (act)
            {
                case Act.Walk:
                    maidX += WalkSpeed * dt;
                    walkPhase += dt;
                    maidFrame = ((int)(walkPhase * 4)) % 2;
                    // 优先级：砍树 → 近战 → 射箭 → 吃东西
                    if (tree != null) { act = Act.Chop; actT = 0; maidFrame = 0; }
                    else if (target != null) { act = Act.Attack; actT = 0; }
                    else if (far != null && shootCd <= 0) { act = Act.Shoot; actT = 0; }
                    else if (hearts <= 4 && food > 0 && !danger && eatCd <= 0) { act = Act.Eat; actT = 0; }
                    break;

                case Act.Shoot:
                    walkPhase = 0;
                    if (actT >= 0.5 && actT - dt < 0.5)
                    {
                        // 朝**目标实际距离**射：这样箭才会真的落到怪身上（之前固定初速，箭在半路就落地了，用户说"没碰到怪物"）
                        double dist = (far != null) ? Math.Max(24.0, far.x - maidX + 4) : 60.0;
                        double tf = 0.45;
                        Arrow ar = new Arrow();
                        ar.x = maidX + 12; ar.y = GroundY - 14;
                        ar.vx = dist / tf;
                        ar.vy = -0.5 * 60 * tf;               // 抛物线：0.45s 后回到同一高度
                        ar.fromMaid = true;
                        arrows.Add(ar);
                        SpawnPuffs(maidX + 10, GroundY - 14, 2, Px.C(0xFFD8D8D8));
                    }
                    if (actT >= 0.62) { act = Act.Walk; actT = 0; shootCd = 4.5 + rnd.NextDouble() * 4; }
                    break;

                case Act.Eat:
                    walkPhase = 0;
                    // 吃东西期间不能移动；被打中会中断（在受伤分支里处理）
                    if (actT >= 1.2)
                    {
                        eatCd = 8 + rnd.NextDouble() * 6;
                        if (food > 0) { food--; hearts = Math.Min(7, hearts + 1); SpawnPuffs(maidX, GroundY - 14, 6, Px.C(0xFF8FE06A)); }
                        act = Act.Walk; actT = 0;
                    }
                    break;

                case Act.Chop:
                    walkPhase = 0;
                    if (actT >= 0.45)
                    {
                        actT = 0;
                        if (tree != null && tree.alive)
                        {
                            tree.hits++;
                            SpawnPuffs(tree.x, GroundY - 22, 5, BARK_D);
                            if (tree.hits >= 3) { tree.alive = false; tree.fallT = 0; Logs = (Logs + 1) & 0xFF; SpawnPuffs(tree.x, GroundY - 18, 12, BARK); }   // 255 → 0 回绕
                        }
                        else act = Act.Walk;
                    }
                    break;

                case Act.Attack:
                    walkPhase = 0;
                    if (actT >= 0.2 && actT - dt < 0.2 && target != null)
                    {
                        target.hp -= 1;
                        SpawnPuffs(target.x, GroundY - 12, 4, Px.C(0xFFD8D8D8));
                        if (target.hp <= 0) { target.state = "dying"; target.t = 0; DropFood(target.x); SpawnPuffs(target.x, GroundY - 12, 8, Px.C(0xFF9AE07A)); }
                    }
                    if (actT >= 0.34) { act = Act.Walk; actT = 0; }
                    break;

                case Act.Hurt:
                    walkPhase = 0;
                    if (actT >= 0.45) { act = Act.Walk; actT = 0; }
                    break;
            }

            // ---- 怪物推进 ----
            for (int i = mobs.Count - 1; i >= 0; i--)
            {
                Mob m = mobs[i];
                m.t += dt;
                if (m.hurt > 0) m.hurt = Math.Max(0, m.hurt - dt);
                if (m.state == "dying") { if (m.t > 0.35) mobs.RemoveAt(i); continue; }
                if (m.state == "hiss")
                {
                    if (m.t >= 0.7)
                    {
                        // 爆炸：贴身扣 3 心（v0.0.11 起不再是必死，给"挣扎"留余地）
                        flash = 1.0;
                        SpawnPuffs(m.x, GroundY - 10, 22, Px.C(0xFFCFCFCF));
                        SpawnPuffs(m.x, GroundY - 10, 10, Px.C(0xFF6B6B6B));
                        if (Math.Abs(m.x - maidX) < 26 && iFrame <= 0)
                        {
                            hearts -= 3;
                            hurtFlash = 0.8;
                            iFrame = 0.9;
                            act = Act.Hurt; actT = 0;
                            if (hearts <= 0) { Die(); return; }
                        }
                        mobs.RemoveAt(i);
                    }
                    continue;
                }
                m.x -= m.speed * dt;
                // 骷髅：进入射程 → 拉弓 0.6s → 射出可见的抛物线箭矢
                if (m.kind == "skeleton" && m.state == "walk")
                {
                    double sd = m.x - maidX;
                    if (sd > 30 && sd < 84 && m.t > 1.0)
                    {
                        m.state = "aim"; m.t = 0;
                        continue;
                    }
                }
                else if (m.kind == "skeleton" && m.state == "aim")
                {
                    if (m.t >= 0.6)
                    {
                        double dist = Math.Max(20.0, m.x - maidX);
                        double tf = 0.7;                                  // 飞行 0.7s
                        Arrow ar = new Arrow();
                        ar.x = m.x - 6; ar.y = GroundY - 20;
                        ar.vx = -dist / tf;
                        ar.vy = -0.5 * 70 * tf;                           // 抛物线：0.7s 后回到同一高度
                        ar.fromMaid = false;
                        arrows.Add(ar);
                        m.state = "walk"; m.t = 0;
                    }
                    continue;
                }
                if (m.kind == "creeper" && m.x - maidX < 12) { m.state = "hiss"; m.t = 0; continue; }
                // 撞到女主
                if (m.x - maidX < 8 && m.x - maidX > -6 && act != Act.Attack && iFrame <= 0)
                {
                    hearts--;
                    hurtFlash = 0.6;
                    iFrame = 0.7;                        // MC 式受伤硬直
                    m.x += 16;                           // 弹开
                    act = Act.Hurt; actT = 0;
                    SpawnPuffs(maidX, GroundY - 12, 5, Px.C(0xFFE07A7A));
                    if (hearts <= 0) { Die(); return; }
                }
                if (m.x < camX - 20) mobs.RemoveAt(i);   // 走出屏幕左侧就回收
            }

            // ---- 箭矢 ----
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                Arrow ar = arrows[i];
                ar.x += ar.vx * dt; ar.y += ar.vy * dt; ar.vy += 70 * dt;
                bool gone = ar.y > GroundY - 2 || ar.x < camX - 30 || ar.x > camX + W + 30;
                if (!gone && !ar.fromMaid && iFrame <= 0 && Math.Abs(ar.x - maidX) < 6 && ar.y > GroundY - 23)
                {
                    hearts--;
                    hurtFlash = 0.6;
                    iFrame = 0.7;
                    if (act == Act.Eat) { act = Act.Hurt; actT = 0; }     // 吃东西被打断
                    SpawnPuffs(maidX, GroundY - 14, 6, Px.C(0xFFE07A7A));
                    gone = true;
                    if (hearts <= 0) { Die(); return; }
                }
                else if (!gone && ar.fromMaid)
                {
                    for (int mi = 0; mi < mobs.Count; mi++)
                    {
                        Mob m = mobs[mi];
                        if (m.state == "dying") continue;
                        if (Math.Abs(m.x - ar.x) < 7 && ar.y > GroundY - 22)
                        {
                            m.hp -= 1;
                            m.hurt = 0.22;                    // 怪物也闪红（用户要求）
                            SpawnPuffs(m.x, GroundY - 14, 4, Px.C(0xFFD8D8D8));
                            if (m.hp <= 0) { m.state = "dying"; m.t = 0; DropFood(m.x); }
                            gone = true;
                            break;
                        }
                    }
                }
                if (gone) arrows.RemoveAt(i);
            }

            // ---- 树 ----
            for (int i = trees.Count - 1; i >= 0; i--)
            {
                Tree tr = trees[i];
                if (!tr.alive) tr.fallT += dt;
                if (!tr.alive && tr.fallT > 3) trees.RemoveAt(i);
                else if (tr.x < camX - 30) trees.RemoveAt(i);
            }

            // ---- 生成 ----
            nextMobT -= dt;
            if (nextMobT <= 0 && mobs.Count < 4)
            {
                nextMobT = 5.5 + rnd.NextDouble() * 3.5;
                double r = rnd.NextDouble();
                string kind = r < 0.30 ? "zombie" : r < 0.55 ? "skeleton" : r < 0.75 ? "spider"
                            : (rnd.NextDouble() < Math.Max(0.15, CreeperChance * 3) ? "creeper" : "zombie");
                Mob m = new Mob();
                m.kind = kind;
                m.hp = kind == "zombie" ? 2 : kind == "creeper" ? 3 : 1;
                m.speed = kind == "spider" ? 11 : kind == "creeper" ? 8 : kind == "skeleton" ? 5.5 : 7;
                m.x = maidX + 110 + rnd.NextDouble() * 30;
                mobs.Add(m);
            }
            nextTreeT -= dt;
            if (nextTreeT <= 0)
            {
                nextTreeT = 6 + rnd.NextDouble() * 5;
                Tree tr = new Tree();
                tr.x = maidX + 40 + rnd.NextDouble() * 30;
                trees.Add(tr);
            }

            camX = maidX - MaidScreenX;
        }

        void Die()
        {
            phase = Phase.Died; t = 0; flash = 1.0;
            Logs = 0;                       // 用户定的：死亡清空原木
            arrows.Clear();
        }

        /// <summary>怪物掉食物（骨头/线/腐肉都算一份口粮）：走近就捡</summary>
        void DropFood(double wx)
        {
            if (food >= 6) return;
            food++;
            SpawnPuffs(wx, GroundY - 12, 5, Px.C(0xFFE8C070));
        }

        /// <summary>
        /// 开发用（--sidetest）：摆一个"血少、有食物、骷髅在射程内"的局面，
        /// 一屏之内就能看到 射箭 / 掉血闪红 / 掏东西回血 —— 这些本来是随机事件，靠抓拍很难验。
        /// </summary>
        public void DevSetup()
        {
            hearts = 2; food = 3; iFrame = 0; shootCd = 0.3; eatCd = 0;
            Mob sk = new Mob();
            sk.kind = "skeleton"; sk.hp = 1; sk.speed = 5.5; sk.x = maidX + 74; sk.t = 1.2;
            mobs.Add(sk);
            Mob zb = new Mob();
            zb.kind = "zombie"; zb.hp = 2; zb.speed = 7; zb.x = maidX + 150;
            mobs.Add(zb);
            Render();
        }

        void SpawnPuffs(double wx, double wy, int n, int color)
        {
            for (int i = 0; i < n; i++)
            {
                Puff p = new Puff();
                p.x = wx + (rnd.NextDouble() - 0.5) * 8;
                p.y = wy + (rnd.NextDouble() - 0.5) * 8;
                p.vx = (rnd.NextDouble() - 0.5) * 34;
                p.vy = -12 - rnd.NextDouble() * 26;
                p.life0 = p.life = 0.5 + rnd.NextDouble() * 0.6;
                p.color = color;
                puffs.Add(p);
            }
        }

        void StepPuffs(double dt)
        {
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                Puff p = puffs[i];
                p.life -= dt;
                if (p.life <= 0) { puffs.RemoveAt(i); continue; }
                p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 60 * dt;
            }
        }

        // ------------------------------------------------------------------ 绘制
        int SX(double worldX) { return (int)Math.Round(worldX - camX); }

        void Render()
        {
            // 天空
            for (int y = 0; y < GroundY; y++)
                Px.Fill(buf, W, H, 0, y, W, 1, Px.Lerp(Px.C(0xFF6FA8FF), Px.C(0xFFBDE2FF), y / (double)GroundY));
            // 方形太阳 + 云
            Px.Fill(buf, W, H, 96, 8, 9, 9, Px.C(0xFFFFF4B0));
            Px.Fill(buf, W, H, 98, 10, 5, 5, Px.C(0xFFFFFBDA));
            int c1 = SX(camX * 0.2 + 40), c2 = SX(camX * 0.2 + 150);
            Cloud(c1, 12); Cloud(c2, 22);

            // 远山（视差 0.35）
            int off1 = (int)(-camX * 0.35);
            for (int k = 0; k < 6; k++)
            {
                int hx = ((off1 + k * 90) % 540 + 540) % 540 - 60;
                Px.Fill(buf, W, H, hx, GroundY - 16, 46, 16, Px.C(0xFF2E6B2A));
                Px.Fill(buf, W, H, hx + 8, GroundY - 22, 30, 8, Px.C(0xFF357A30));
            }
            // 近处灌木（视差 0.6）
            int off2 = (int)(-camX * 0.6);
            for (int k = 0; k < 8; k++)
            {
                int bx = ((off2 + k * 70) % 560 + 560) % 560 - 40;
                Px.Fill(buf, W, H, bx, GroundY - 7, 20, 7, Px.C(0xFF3F8A2A));
                Px.Fill(buf, W, H, bx + 4, GroundY - 10, 10, 4, Px.C(0xFF4C9A33));
            }

            // 地面
            Px.Fill(buf, W, H, 0, GroundY, W, H - GroundY, DIRT);
            Px.Fill(buf, W, H, 0, GroundY, W, 4, GRASS);
            for (int x = 0; x < W; x++)
            {
                int wx = (int)(camX + x);
                if ((wx * 7) % 5 == 0) Px.Put(buf, W, H, x, GroundY + 4, GRASS_D);
                if ((wx * 13) % 7 == 0) Px.Put(buf, W, H, x, GroundY + 6, DIRT_D);
                if ((wx * 29) % 23 == 0) Px.Fill(buf, W, H, x, GroundY + 12, 2, 2, STONE);
            }

            // 树（先画，让角色站在前面）
            for (int i = 0; i < trees.Count; i++) DrawTree(trees[i]);

            // 实体：按世界坐标从左到右画，保证遮挡顺序自然
            var order = new List<object>();
            for (int i = 0; i < mobs.Count; i++) order.Add(mobs[i]);
            order.Add("maid");
            order.Sort(delegate(object a, object b)
            {
                double ax = a is Mob ? ((Mob)a).x : maidX;
                double bx = b is Mob ? ((Mob)b).x : maidX;
                return ax.CompareTo(bx);
            });
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] is Mob) DrawMob((Mob)order[i]);
                else DrawMaid();
            }

            // 粒子
            for (int i = 0; i < puffs.Count; i++)
            {
                Puff p = puffs[i];
                double a = p.life / p.life0;
                if (a < 0.3) continue;
                Px.Fill(buf, W, H, SX(p.x), (int)p.y, 2, 2, p.color);
            }

            // 箭矢
            for (int i = 0; i < arrows.Count; i++)
            {
                Arrow ar = arrows[i];
                int ax = SX(ar.x), ay = (int)ar.y;
                Px.Fill(buf, W, H, ax - 2, ay, 5, 1, Px.C(0xFF4A3A28));       // 箭杆
                Px.Fill(buf, W, H, ax + (ar.fromMaid ? 2 : -3), ay - 1, 2, 3, Px.C(0xFFE8E8E8));  // 尾羽
                Px.Put(buf, W, H, ax + (ar.fromMaid ? -3 : 3), ay, Px.C(0xFF9A9A9A));              // 箭头
            }

            // 食物计数（右下角，热键栏上方）
            if (food > 0)
            {
                Px.Fill(buf, W, H, 96, 44, 4, 3, Px.C(0xFFA0522D));
                Px.Fill(buf, W, H, 97, 47, 2, 3, Px.C(0xFF8B4513));
                Px.Fill(buf, W, H, 96, 50, 2, 1, Px.C(0xFFE8E0C8));
                Px.Text(buf, W, H, "X" + food, 102, 45, Px.C(0xFFFFFFFF));
            }

            // 死亡 / 白闪
            if (phase == Phase.Died)
                for (int i = 0; i < buf.Length; i++) buf[i] = Px.Blend(buf[i], Px.C(0xFF7A0000), 0.88);
            if (flash > 0)
            {
                double fa = Math.Min(1.0, flash);
                for (int i = 0; i < buf.Length; i++) buf[i] = Px.Blend(buf[i], Px.C(0xFFFFFFFF), fa);
            }

            // HUD（死亡时不画，和 MC 一致）
            if (phase != Phase.Died) hud.Draw(buf, t, Math.Max(0, hearts), Logs, false);   // 2D 屏不画准星（屏幕中间那个白十字已去掉）

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, W, H), buf, W * 4, 0);
        }

        void Cloud(int x, int y)
        {
            Px.Fill(buf, W, H, x, y, 18, 4, Px.C(0xFFFFFFFF));
            Px.Fill(buf, W, H, x + 4, y - 3, 10, 3, Px.C(0xFFFFFFFF));
            Px.Fill(buf, W, H, x + 2, y + 4, 13, 2, Px.C(0xF0FFFFFF));
        }

        void DrawTree(Tree tr)
        {
            int x = SX(tr.x);
            if (x < -40 || x > W + 20) return;
            int baseY = GroundY;
            if (tr.alive)
            {
                Px.Fill(buf, W, H, x - 3, baseY - 26, 6, 26, BARK);
                Px.Fill(buf, W, H, x - 3, baseY - 26, 2, 26, Px.C(0xFF7E5A2C));
                Px.Fill(buf, W, H, x + 1, baseY - 26, 1, 26, BARK_D);
                // 树冠：方块叶簇
                for (int yy = baseY - 40; yy < baseY - 20; yy += 2)
                    for (int xx = x - 14; xx < x + 12; xx += 2)
                    {
                        int d = Math.Abs(xx - x) + Math.Abs(yy - (baseY - 30)) * 2;
                        if (d > 20) continue;
                        if (((xx * 13 + yy * 7) % 19) == 0) continue;
                        Px.Fill(buf, W, H, xx, yy, 2, 2, ((xx + yy) % 4 == 0) ? LEAF_L : ((xx / 2 + yy / 2) % 3 == 0 ? LEAF_D : LEAF));
                    }
                // 砍痕
                for (int i = 0; i < tr.hits * 2; i++)
                    Px.Fill(buf, W, H, x - 2 + (i % 2) * 2, baseY - 10 - (i % 3) * 3, 2, 2, Px.C(0xFF3A2410));
            }
            else
            {
                // 倒下：树干横躺 + 树冠落地
                Px.Fill(buf, W, H, x - 18, baseY - 6, 22, 5, BARK);
                Px.Fill(buf, W, H, x - 18, baseY - 6, 22, 2, Px.C(0xFF7E5A2C));
                for (int xx = x - 30; xx < x - 12; xx += 2)
                    for (int yy = baseY - 12; yy < baseY - 2; yy += 2)
                        Px.Fill(buf, W, H, xx, yy, 2, 2, ((xx + yy) % 3 == 0) ? LEAF_D : LEAF);
            }
        }

        void DrawMaid()
        {
            int ox = SX(maidX), oy = GroundY - 16;
            if (panicT > 0) ox += (((int)(t * 20)) % 2 == 0) ? 1 : -1;   // 僵住发抖
            int bob = (act == Act.Walk && maidFrame == 1) ? 1 : 0;
            oy += bob;
            // 呆毛
            Px.Fill(buf, W, H, ox + 6, oy - 3, 1, 3, HAIR2);
            Px.Fill(buf, W, H, ox + 7, oy - 2, 1, 2, HAIR2);
            // 头发（上窄下宽，别像个头盔）
            Px.Fill(buf, W, H, ox + 4, oy - 2, 4, 1, HAIR);
            Px.Fill(buf, W, H, ox + 2, oy - 1, 8, 2, HAIR);
            Px.Fill(buf, W, H, ox + 1, oy + 1, 10, 3, HAIR);
            // 女仆头饰（白色荷叶边）
            Px.Fill(buf, W, H, ox + 3, oy - 2, 6, 1, WHITE);
            // 脸
            Px.Fill(buf, W, H, ox + 3, oy + 1, 6, 4, SKIN);
            Px.Fill(buf, W, H, ox + 3, oy + 1, 6, 1, HAIR);
            Px.Put(buf, W, H, ox + 4, oy + 3, EYE);
            Px.Put(buf, W, H, ox + 7, oy + 3, EYE);
            Px.Put(buf, W, H, ox + 5, oy + 4, Px.C(0xFFE09A9A));
            Px.Put(buf, W, H, ox + 3, oy + 4, BLUSH);
            Px.Put(buf, W, H, ox + 8, oy + 4, BLUSH);
            // 侧发垂到肩 + 蓝蝴蝶结
            Px.Fill(buf, W, H, ox + 1, oy + 4, 2, 5, HAIR2);
            Px.Fill(buf, W, H, ox + 9, oy + 4, 2, 5, HAIR2);
            Px.Fill(buf, W, H, ox + 9, oy - 1, 2, 1, Px.C(0xFF5A8FD8));
            // 身体：藏青连衣裙 + 白围裙
            Px.Fill(buf, W, H, ox + 3, oy + 5, 6, 6, NAVY);
            Px.Fill(buf, W, H, ox + 4, oy + 6, 4, 4, WHITE);
            // 手臂：用稍亮的藏青 + 白袖口，否则和裙子糊在一起
            int ARM = Px.C(0xFF3E5488);
            if (act == Act.Attack)
            {
                Px.Fill(buf, W, H, ox + 9, oy + 1, 2, 5, ARM);
                Px.Fill(buf, W, H, ox + 11, oy - 1, 1, 5, Px.C(0xFFD8D8D8));   // 剑
                Px.Fill(buf, W, H, ox + 10, oy + 3, 3, 1, Px.C(0xFF8B5A2B));   // 护手
            }
            else if (act == Act.Chop)
            {
                int ph = (int)(actT * 3) % 2;
                Px.Fill(buf, W, H, ox + 9, oy + (ph == 0 ? 2 : 5), 2, 4, ARM);
                Px.Fill(buf, W, H, ox + 11, oy + (ph == 0 ? 0 : 6), 1, 4, Px.C(0xFFD8D8D8));
            }
            else if (act == Act.Shoot)
            {
                // 拉弓：左手前伸持弓，右手拉弦（0.5s 后放箭）
                Px.Fill(buf, W, H, ox + 9, oy + 4, 4, 2, ARM);
                Px.Fill(buf, W, H, ox + 13, oy + 1, 1, 8, Px.C(0xFF8B5A2B));    // 弓身
                Px.Fill(buf, W, H, ox + 13, oy + 1, 3, 1, Px.C(0xFF8B5A2B));
                Px.Fill(buf, W, H, ox + 13, oy + 8, 3, 1, Px.C(0xFF8B5A2B));
                double pull = Math.Min(1.0, actT / 0.5);
                Px.Fill(buf, W, H, ox + 8 - (int)(pull * 3), oy + 4, 2, 2, Px.C(0xFFF0D0A0));  // 手
                Px.Fill(buf, W, H, ox + 9, oy + 5, 4, 1, Px.C(0xFFE8E8E8));     // 箭
            }
            else if (act == Act.Eat)
            {
                // 吃东西：一只手举到嘴边 + 咀嚼抖动；被打算中断
                int chew = ((int)(actT * 8) % 2 == 0) ? 0 : 1;
                Px.Fill(buf, W, H, ox + 8, oy + 4 + chew, 3, 2, ARM);
                Px.Fill(buf, W, H, ox + 10, oy + 2 + chew, 3, 3, Px.C(0xFFB0603A));   // 食物
                Px.Fill(buf, W, H, ox + 11, oy + 2 + chew, 2, 1, Px.C(0xFFE8C070));
                if (chew == 0) Px.Fill(buf, W, H, ox + 5, oy + 6, 3, 1, Px.C(0xFFB03040));  // 张嘴
            }
            else
            {
                Px.Fill(buf, W, H, ox + 1, oy + 6, 2, 4, ARM);
                Px.Fill(buf, W, H, ox + 9, oy + 6, 2, 4, ARM);
                Px.Put(buf, W, H, ox + 2, oy + 9, WHITE);      // 袖口
                Px.Put(buf, W, H, ox + 9, oy + 9, WHITE);
            }
            // 腿
            int lg = (act == Act.Walk && maidFrame == 1) ? 1 : 0;
            Px.Fill(buf, W, H, ox + 4 - lg, oy + 11, 2, 3, WHITE);
            Px.Fill(buf, W, H, ox + 7 + lg, oy + 11, 2, 3, WHITE);
            Px.Fill(buf, W, H, ox + 3 - lg, oy + 14, 3, 1, SHOE);
            Px.Fill(buf, W, H, ox + 7 + lg, oy + 14, 3, 1, SHOE);
            // 鲸鱼尾巴：甩在身后（左侧），末端**分叉成两片**（深一点的蓝），走路时上下摆动
            {
                int sway = 0;
                if (act == Act.Walk) sway = (((int)(walkPhase * 4)) % 2 == 0) ? 0 : 1;
                else if (act == Act.Eat || act == Act.Chop) sway = ((int)(t * 6) % 2 == 0) ? 0 : 1;
                int ty = oy + 9 + sway;
                Px.Fill(buf, W, H, ox - 2, ty, 3, 2, TAIL);          // 尾柄
                Px.Fill(buf, W, H, ox - 4, ty - 1, 2, 1, TAIL);      // 上叉
                Px.Fill(buf, W, H, ox - 4, ty + 2, 2, 1, TAIL);      // 下叉（中间留缝 = 分叉）
            }
            // 受伤闪红（MC 的受伤就是整只变红一下）
            if (hurtFlash > 0)
            {
                double k = Math.Min(0.75, hurtFlash * 1.6);
                for (int y = oy - 3; y < oy + 16; y++)
                    for (int x = ox; x < ox + 13; x++)
                        if (x >= 0 && y >= 0 && x < W && y < H)
                            buf[y * W + x] = Px.Blend(buf[y * W + x], Px.C(0xFFFF2A2A), k);
            }
            // 被叫去干活：头顶"！" + 汗滴
            if (panicT > 0)
            {
                Px.Fill(buf, W, H, ox + 5, oy - 10, 2, 5, Px.C(0xFFFFFFFF));
                Px.Fill(buf, W, H, ox + 5, oy - 3, 2, 2, Px.C(0xFFFFFFFF));
                int sw = (int)((t * 12) % 4);
                Px.Fill(buf, W, H, ox + 12, oy - 6 + sw, 2, 4, Px.C(0xFF9FD8F0));
                Px.Fill(buf, W, H, ox - 4, oy - 2 + (3 - sw), 2, 3, Px.C(0xFF9FD8F0));
            }
        }

        void DrawMob(Mob m)
        {
            int ox = SX(m.x), oy = GroundY - 15;
            bool dying = m.state == "dying";
            double fade = dying ? Math.Max(0, 1 - m.t / 0.35) : 1.0;
            if (fade <= 0) return;
            DrawMobBody(m, ox, oy);
            // 受击闪红：整只压一层红（和她的受伤表现一致）
            if (m.hurt > 0)
            {
                double k = Math.Min(0.7, m.hurt * 3.2);
                for (int y = 2; y < 20; y++)
                    for (int x = -3; x < 12; x++)
                    {
                        int px2 = ox + x, py2 = oy + y;
                        if (px2 >= 0 && py2 >= 0 && px2 < W && py2 < H)
                            buf[py2 * W + px2] = Px.Blend(buf[py2 * W + px2], Px.C(0xFFFF2A2A), k);
                    }
            }
        }

        void DrawMobBody(Mob m, int ox, int oy)
        {
            switch (m.kind)
            {
                case "zombie":
                    Px.Fill(buf, W, H, ox + 2, oy, 6, 5, Px.C(0xFF4C8A3F));
                    Px.Put(buf, W, H, ox + 3, oy + 2, Px.C(0xFF14200F));
                    Px.Put(buf, W, H, ox + 6, oy + 2, Px.C(0xFF14200F));
                    Px.Fill(buf, W, H, ox + 2, oy + 5, 6, 6, Px.C(0xFF2E6E6A));
                    Px.Fill(buf, W, H, ox - 1, oy + 5, 3, 2, Px.C(0xFF4C8A3F));   // 前伸的手
                    Px.Fill(buf, W, H, ox + 8, oy + 5, 3, 2, Px.C(0xFF4C8A3F));
                    Px.Fill(buf, W, H, ox + 2, oy + 11, 2, 4, Px.C(0xFF2B3A56));
                    Px.Fill(buf, W, H, ox + 6, oy + 11, 2, 4, Px.C(0xFF2B3A56));
                    // 注：原来这里还有两条伸到身体外的深蓝像素条（ox-1 / ox+8，各 3px），
                    // 看起来像"裤子左右各伸出一根蓝条"，用户指出后已删。
                    break;
                case "skeleton":
                    Px.Fill(buf, W, H, ox + 2, oy, 6, 5, Px.C(0xFFD8D8D0));
                    Px.Put(buf, W, H, ox + 3, oy + 2, Px.C(0xFF1A1A1A));
                    Px.Put(buf, W, H, ox + 6, oy + 2, Px.C(0xFF1A1A1A));
                    Px.Fill(buf, W, H, ox + 3, oy + 5, 4, 6, Px.C(0xFFD8D8D0));
                    Px.Fill(buf, W, H, ox + 3, oy + 6, 4, 1, Px.C(0xFF6A6A62));
                    Px.Fill(buf, W, H, ox + 3, oy + 8, 4, 1, Px.C(0xFF6A6A62));
                    Px.Fill(buf, W, H, ox + 1, oy + 5, 1, 5, Px.C(0xFFD8D8D0));
                    Px.Fill(buf, W, H, ox + 8, oy + 5, 1, 5, Px.C(0xFFD8D8D0));
                    Px.Fill(buf, W, H, ox + 3, oy + 11, 1, 4, Px.C(0xFFD8D8D0));
                    Px.Fill(buf, W, H, ox + 6, oy + 11, 1, 4, Px.C(0xFFD8D8D0));
                    break;
                case "spider":
                    for (int i = 0; i < 4; i++)
                    {
                        Px.Fill(buf, W, H, ox - 2, oy + 6 + i * 2, 3, 1, Px.C(0xFF1E1A18));
                        Px.Fill(buf, W, H, ox + 8, oy + 6 + i * 2, 3, 1, Px.C(0xFF1E1A18));
                    }
                    Px.Fill(buf, W, H, ox + 1, oy + 6, 8, 6, Px.C(0xFF24201E));
                    Px.Fill(buf, W, H, ox, oy + 8, 2, 3, Px.C(0xFF1A1716));
                    Px.Fill(buf, W, H, ox + 4, oy + 7, 2, 2, Px.C(0xFFC03030));
                    Px.Fill(buf, W, H, ox + 6, oy + 7, 2, 2, Px.C(0xFFC03030));
                    break;
                default:   // creeper：共用 Px.Creeper（正视、长方形、脚踩地）
                    {
                        bool hiss = m.state == "hiss";
                        // 嘶嘶时整只闪白：把画法里的颜色整体提亮
                        if (hiss && ((int)(t * 30) % 2 == 0))
                        {
                            Px.Creeper(buf, W, H, ox, GroundY, 0);
                            for (int y = GroundY - 21; y < GroundY; y++)
                                for (int x = ox; x < ox + 8; x++)
                                    if (x >= 0 && y >= 0 && x < W && y < H)
                                        buf[y * W + x] = Px.Blend(buf[y * W + x], Px.C(0xFFEEEEEE), 0.75);
                        }
                        else Px.Creeper(buf, W, H, ox, GroundY, 0);
                        if (hiss)
                        {
                            int r = (int)(m.t * 40) + 6;
                            for (int a = 0; a < 360; a += 20)
                            {
                                double rad = a * Math.PI / 180.0;
                                Px.Put(buf, W, H, ox + 4 + (int)(Math.Cos(rad) * r), GroundY - 10 + (int)(Math.Sin(rad) * r * 0.6), Px.C(0xFFFFFFFF));
                            }
                        }
                    }
                    break;
            }
        }
    }
}
