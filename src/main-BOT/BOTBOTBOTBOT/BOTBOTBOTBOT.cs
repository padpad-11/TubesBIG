using System;
using System.Linq;
using Robocode.TankRoyale.BotApi.Graphics;
using System.Collections.Generic;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class BOTBOTBOTBOT : Bot
{
    // =========================
    // Constants
    // =========================

    private const double WALL_MARGIN = 25;

    private const double GUN_FACTOR = 5;
    private const double MIN_FIRE_ENERGY = 12;
    private const double RADAR_LOCK = 0.7;

    private const double MIN_MOVE_RADIUS = 200;
    private const double MAX_MOVE_RADIUS = 300;
    private const int MOVE_POINTS = 36;

    private const double EPSILON = 1e-6;

    private const int MAX_SAG_HITS = 3;

    private const int NGRAM_SIZE = 4;

    private const int BULLET_BOUNDARY = 50;

    private const int ENEMY_GRAVITY = 300;
    private const int BULLET_GRAVITY = 10;
    private const int LAST_POSITION_GRAVITY = 10;
    private const int CORNER_GRAVITY = 100;

    private const double ENERGY_THRESHOLD = 1.3;

    // =========================
    // Runtime Variables
    // =========================

    private static int currentTargetId;

    private static double currentTargetDistance;
    private static double scannedEnemyDistance;

    private static double moveTargetX;
    private static double moveTargetY;

    private static int sagDirection = 1;
    private static int sagHitCounter;

    private static bool disableSag;

    private readonly Random random = new Random();

    private static readonly Dictionary<int, EnemyInfo> enemies =
        new Dictionary<int, EnemyInfo>();

    private static List<VirtualBullet> enemyBullets;
    private static List<MyVirtualBullet> myBullets;

    // =========================
    // Main
    // =========================

    static void Main()
    {
        new BOTBOTBOTBOT().Start();
    }

    public BOTBOTBOTBOT() : base(BotInfo.FromFile("BOTBOTBOTBOT.json")) { }

    // =========================
    // Run
    // =========================

    public override void Run()
    {
        Console.WriteLine("BOTBOTBOTBOT | Ronde EZ ");

        RadarColor = Color.White;
        TracksColor = Color.White;
        GunColor = Color.White;

        AdjustGunForBodyTurn = true;
        AdjustRadarForGunTurn = true;
        AdjustRadarForBodyTurn = true;

        SetTurnRadarRight(double.PositiveInfinity);

        currentTargetDistance = double.PositiveInfinity;
        scannedEnemyDistance = double.PositiveInfinity;

        enemyBullets = new List<VirtualBullet>();
        myBullets = new List<MyVirtualBullet>();

        disableSag = false;
        sagHitCounter = 0;
    }

    // =========================
    // Tick
    // =========================

    public override void OnTick(TickEvent e)
    {
        UpdateColors();

        DrawEnemyBullets();
        DrawMyBullets();

        if (sagHitCounter > MAX_SAG_HITS)
            disableSag = true;

        if (!disableSag && EnemyCount == 1 && currentTargetDistance > 250)
            return;

        UpdateMovement();
    }

    // =========================
    // Scan
    // =========================

    public override void OnScannedBot(ScannedBotEvent e)
    {
        EnemyInfo enemy = GetEnemy(e.ScannedBotId);

        enemy.LastX = e.X;
        enemy.LastY = e.Y;
        enemy.IsAlive = true;

        HandleTargetSelection(e);

        HandleRadarLock(e);

        HandleFireControl(e);

        HandleEnemyBulletDetection(e, enemy);

        UpdateEnemyMovementState(e, enemy);

        if (enemy.AimModes.IndexOf(enemy.AimModes.Max()) != 0)
        {
            SetTurnGunLeft(GunBearingTo(e.X, e.Y));
            return;
        }

        PredictiveAim(e, enemy);
    }

    // =========================
    // Fired
    // =========================

    public override void OnBulletFired(BulletFiredEvent e)
    {
        AddMyBullet(
            X,
            Y,
            e.Bullet.Speed,
            e.Bullet.Power,
            GunDirection * Math.PI / 180,
            currentTargetId,
            0
        );

        EnemyInfo enemy = enemies[currentTargetId];

        AddMyBullet(
            X,
            Y,
            e.Bullet.Speed,
            e.Bullet.Power,
            DirectionTo(enemy.LastX, enemy.LastY) * Math.PI / 180,
            currentTargetId,
            1
        );
    }

    // =========================
    // Hit By Bullet
    // =========================

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        if (EnemyCount == 1)
            sagHitCounter++;
    }

    // =========================
    // Bot Death
    // =========================

    public override void OnBotDeath(BotDeathEvent e)
    {
        enemies[e.VictimId].IsAlive = false;

        if (e.VictimId == currentTargetId)
            currentTargetDistance = double.PositiveInfinity;
    }

    // =========================
    // Colors
    // =========================

    private void UpdateColors()
    {
        BodyColor = ScanColor;
        BulletColor = ScanColor;
    }

    // =========================
    // Bullet Rendering
    // =========================

    private void DrawEnemyBullets()
    {
        var graphics = Graphics;

        for (int i = enemyBullets.Count - 1; i >= 0; i--)
        {
            VirtualBullet bullet = enemyBullets[i];

            bullet.X += bullet.Speed * Math.Cos(bullet.Direction);
            bullet.Y += bullet.Speed * Math.Sin(bullet.Direction);

            graphics.FillRectangle(
                (float)bullet.X,
                (float)bullet.Y,
                (float)(3 * bullet.Power),
                (float)(3 * bullet.Power)
            );

            if (IsOutsideArena(bullet.X, bullet.Y))
            {
                enemyBullets.RemoveAt(i);
            }
            else
            {
                enemyBullets[i] = bullet;
            }
        }
    }

    private void DrawMyBullets()
    {
        var graphics = Graphics;

        for (int i = myBullets.Count - 1; i >= 0; i--)
        {
            VirtualBullet bullet = myBullets[i].Data;

            bullet.X += bullet.Speed * Math.Cos(bullet.Direction);
            bullet.Y += bullet.Speed * Math.Sin(bullet.Direction);

            EnemyInfo enemy = enemies[myBullets[i].TargetId];

            if (Distance(enemy.LastX, enemy.LastY, bullet.X, bullet.Y) < 18)
            {
                enemy.AimModes[myBullets[i].Type] += 5;
                myBullets.RemoveAt(i);
            }
            else if (IsOutsideArena(bullet.X, bullet.Y))
            {
                enemy.AimModes[myBullets[i].Type]--;
                myBullets.RemoveAt(i);
            }
            else
            {
                myBullets[i].Data = bullet;
            }
        }
    }

    // =========================
    // Movement
    // =========================

    private void UpdateMovement()
    {
        double bestX = X;
        double bestY = Y;

        double lowestGravity = double.PositiveInfinity;

        for (int i = 0; i < MOVE_POINTS; i++)
        {
            double angle = (2 * Math.PI / MOVE_POINTS) * i;

            for (int j = 0; j <= 1; j++)
            {
                double radius = Math.Sqrt(
                    j * (MAX_MOVE_RADIUS * MAX_MOVE_RADIUS -
                    MIN_MOVE_RADIUS * MIN_MOVE_RADIUS) +
                    MIN_MOVE_RADIUS * MIN_MOVE_RADIUS
                );

                double candidateX = X + radius * Math.Cos(angle);
                double candidateY = Y + radius * Math.Sin(angle);

                if (IsNearWall(candidateX, candidateY))
                    continue;

                double gravity = CalculateGravity(candidateX, candidateY);

                if (gravity >= lowestGravity)
                    continue;

                lowestGravity = gravity;

                bestX = candidateX;
                bestY = candidateY;
            }
        }

        if (lowestGravity < CalculateGravity(moveTargetX, moveTargetY) * 0.9)
        {
            moveTargetX = bestX;
            moveTargetY = bestY;
        }

        double turnRadians = BearingTo(moveTargetX, moveTargetY) * Math.PI / 180;

        SetTurnLeft(Math.Tan(turnRadians) * 180 / Math.PI);

        SetForward(
            DistanceTo(moveTargetX, moveTargetY) *
            Math.Cos(turnRadians)
        );
    }

    // =========================
    // Target
    // =========================

    private void HandleTargetSelection(ScannedBotEvent e)
    {
        double distance = scannedEnemyDistance = DistanceTo(e.X, e.Y);

        if (distance < currentTargetDistance)
        {
            currentTargetId = e.ScannedBotId;
        }
        else if (e.ScannedBotId != currentTargetId && GunHeat != 0)
        {
            return;
        }

        currentTargetDistance = distance;
    }

    // =========================
    // Radar
    // =========================

    private void HandleRadarLock(ScannedBotEvent e)
    {
        double radarTurn =
            double.PositiveInfinity *
            NormalizeRelativeAngle(RadarBearingTo(e.X, e.Y));

        if (!double.IsNaN(radarTurn) &&
            (GunHeat < RADAR_LOCK || EnemyCount == 1))
        {
            SetTurnRadarLeft(radarTurn);
        }
    }

    // =========================
    // Fire
    // =========================

    private void HandleFireControl(ScannedBotEvent e)
    {
        double power =
            Energy / DistanceTo(e.X, e.Y) * GUN_FACTOR;

        if (GunTurnRemaining == 0 &&
            (Energy > MIN_FIRE_ENERGY ||
            DistanceTo(e.X, e.Y) < 50))
        {
            SetFire(power);
        }
    }

    // =========================
    // Enemy Bullet Detection
    // =========================

    private void HandleEnemyBulletDetection(
        ScannedBotEvent e,
        EnemyInfo enemy
    )
    {
        double energyDrop = enemy.LastEnergy - e.Energy;

        if (energyDrop > 0.11 && energyDrop <= 3)
        {
            AddEnemyBullet(
                e.X,
                e.Y,
                CalcBulletSpeed(energyDrop),
                energyDrop,
                (180 + DirectionTo(e.X, e.Y)) * Math.PI / 180
            );

            AddLinearEnemyBullet(
                e.X,
                e.Y,
                CalcBulletSpeed(energyDrop),
                energyDrop
            );

            HandleSagMovement(e, energyDrop);
        }

        enemy.LastEnergy = e.Energy;
    }

    // =========================
    // Sag
    // =========================

    private void HandleSagMovement(
        ScannedBotEvent e,
        double energyDrop
    )
    {
        if (disableSag)
            return;

        if (EnemyCount != 1)
            return;

        if (DistanceRemaining != 0)
            return;

        if (IsNearWall(X, Y))
        {
            sagDirection = -sagDirection;
            sagHitCounter = 0;
        }

        double turn =
            (
                BearingTo(e.X, e.Y) +
                (90 - 15 * (currentTargetDistance / 1000)) *
                sagDirection
            ) * Math.PI / 180;

        SetTurnLeft(Math.Tan(turn) * 180 / Math.PI);

        SetForward(
            (3 + (int)(energyDrop * 1.999999)) *
            8 *
            Math.Sign(Math.Cos(turn))
        );
    }

    // =========================
    // Enemy State
    // =========================

    private void UpdateEnemyMovementState(
        ScannedBotEvent e,
        EnemyInfo enemy
    )
    {
        double directionRadians =
            e.Direction * Math.PI / 180.0;

        double speed = e.Speed;

        double acceleration =
            enemy.HasPrevious
            ? speed - enemy.LastSpeed
            : 0;

        enemy.LastSpeed = speed;

        double angularVelocity =
            enemy.HasPrevious
            ? (directionRadians - enemy.LastDirection + Math.PI) %
              (2 * Math.PI) - Math.PI
            : 0;

        enemy.LastDirection = directionRadians;

        State state = new State(
            angularVelocity,
            speed,
            acceleration
        );

        enemy.StateHistory.Add(state);

        if (enemy.StateHistory.Count >= NGRAM_SIZE)
        {
            List<State> context =
                enemy.StateHistory.GetRange(
                    enemy.StateHistory.Count - (NGRAM_SIZE - 1),
                    NGRAM_SIZE - 1
                );

            StateSequence key =
                new StateSequence(context);

            if (!enemy.NgramTree.ContainsKey(key))
            {
                enemy.NgramTree[key] =
                    new FrequencyTree();
            }

            enemy.NgramTree[key].Add(state);
        }

        enemy.HasPrevious = true;
    }

    // =========================
    // Prediction
    // =========================

    private void PredictiveAim(
        ScannedBotEvent e,
        EnemyInfo enemy
    )
    {
        double firePower =
            Energy / DistanceTo(e.X, e.Y) * GUN_FACTOR;

        double bulletSpeed =
            CalcBulletSpeed(firePower);

        double predictedX = e.X;
        double predictedY = e.Y;

        double predictedDirection =
            e.Direction * Math.PI / 180;

        double predictedSpeed = e.Speed;

        double simulatedAngularVelocity =
            enemy.HasPrevious
            ? enemy.LastDirection
            : 0;

        State currentState =
            enemy.StateHistory.Last();

        int tick = 0;

        List<State> context = null;

        if (enemy.StateHistory.Count >= NGRAM_SIZE - 1)
        {
            context = new List<State>(
                enemy.StateHistory.GetRange(
                    enemy.StateHistory.Count - (NGRAM_SIZE - 1),
                    NGRAM_SIZE - 1
                )
            );
        }

        while (
            tick * bulletSpeed <
            DistanceTo(predictedX, predictedY) &&
            tick < 100
        )
        {
            if (context != null)
            {
                StateSequence key =
                    new StateSequence(context);

                if (enemy.NgramTree.ContainsKey(key))
                {
                    State next =
                        enemy.NgramTree[key]
                        .GetMostFrequent();

                    simulatedAngularVelocity =
                        next.AngularVelocity / 1024.0;

                    predictedSpeed += next.Acceleration;

                    context.RemoveAt(0);
                    context.Add(next);
                }
            }

            predictedDirection += simulatedAngularVelocity;

            predictedX +=
                predictedSpeed *
                Math.Cos(predictedDirection);

            predictedY +=
                predictedSpeed *
                Math.Sin(predictedDirection);

            tick++;
        }

        predictedX = Math.Max(
            WALL_MARGIN,
            Math.Min(ArenaWidth - WALL_MARGIN, predictedX)
        );

        predictedY = Math.Max(
            WALL_MARGIN,
            Math.Min(ArenaHeight - WALL_MARGIN, predictedY)
        );

        DrawPrediction(predictedX, predictedY);

        double gunTurn =
            GunBearingTo(predictedX, predictedY);

        SetTurnGunLeft(gunTurn);
    }

    // =========================
    // Draw Prediction
    // =========================

    private void DrawPrediction(double x, double y)
    {
        Graphics.DrawRectangle(
            (float)x,
            (float)y,
            20,
            20
        );
    }

    // =========================
    // Gravity
    // =========================

    private double CalculateGravity(
        double x,
        double y
    )
    {
        double gravity = 0;

        foreach (EnemyInfo enemy in enemies.Values)
        {
            if (!enemy.IsAlive)
                continue;

            gravity +=
                ENEMY_GRAVITY *
                (enemy.LastEnergy - ENERGY_THRESHOLD) /
                (
                    DistanceSquared(
                        x,
                        y,
                        enemy.LastX,
                        enemy.LastY
                    ) + EPSILON
                );
        }

        foreach (VirtualBullet bullet in enemyBullets)
        {
            Line2D line = new Line2D(
                bullet.X - Math.Cos(bullet.Direction) * 10000,
                bullet.Y - Math.Sin(bullet.Direction) * 10000,
                bullet.X + Math.Cos(bullet.Direction) * 10000,
                bullet.Y + Math.Sin(bullet.Direction) * 10000
            );

            double distance =
                line.DistanceToPoint(x, y);

            gravity +=
                BULLET_GRAVITY *
                bullet.Power /
                (distance * distance + EPSILON);
        }

        gravity +=
            LAST_POSITION_GRAVITY *
            random.NextDouble() /
            (
                Math.Pow(DistanceTo(x, y), 2) +
                EPSILON
            );

        if (currentTargetId != 0)
        {
            gravity +=
                currentTargetDistance -
                DistanceTo(
                    enemies[currentTargetId].LastX,
                    enemies[currentTargetId].LastY
                );
        }

        gravity += CORNER_GRAVITY /
            DistanceSquared(x, y, 0, 0);

        gravity += CORNER_GRAVITY /
            DistanceSquared(x, y, 0, ArenaHeight);

        gravity += CORNER_GRAVITY /
            DistanceSquared(x, y, ArenaWidth, 0);

        gravity += CORNER_GRAVITY /
            DistanceSquared(x, y, ArenaWidth, ArenaHeight);

        return gravity;
    }

    // =========================
    // Add Bullets
    // =========================

    private void AddEnemyBullet(
        double x,
        double y,
        double speed,
        double power,
        double direction
    )
    {
        enemyBullets.Add(
            new VirtualBullet
            {
                X = x + 2 * speed * Math.Cos(direction),
                Y = y + 2 * speed * Math.Sin(direction),
                Speed = speed,
                Direction = direction,
                Power = power
            }
        );
    }

    private void AddLinearEnemyBullet(
        double x,
        double y,
        double speed,
        double power
    )
    {
        double bulletVelocity = CalcBulletSpeed(power);

        double myDirection =
            Direction * Math.PI / 180;

        double velocityX =
            Speed * Math.Cos(myDirection);

        double velocityY =
            Speed * Math.Sin(myDirection);

        double a =
            velocityX * velocityX +
            velocityY * velocityY -
            bulletVelocity * bulletVelocity;

        double b =
            2 *
            (
                velocityX * (X - x) +
                velocityY * (Y - y)
            );

        double c =
            Math.Pow(X - x, 2) +
            Math.Pow(Y - y, 2);

        double discriminant =
            b * b - 4 * a * c;

        double t1 =
            (-b + Math.Sqrt(discriminant)) /
            (2 * a);

        double t2 =
            (-b - Math.Sqrt(discriminant)) /
            (2 * a);

        double t = Math.Max(t1, t2);

        double predictedX =
            X + velocityX * t;

        double predictedY =
            Y + velocityY * t;

        double direction =
            Math.Atan2(
                predictedY - y,
                predictedX - x
            );

        enemyBullets.Add(
            new VirtualBullet
            {
                X = x + 2 * speed * Math.Cos(direction),
                Y = y + 2 * speed * Math.Sin(direction),
                Speed = speed,
                Direction = direction,
                Power = power * 2
            }
        );
    }

    private void AddMyBullet(
        double x,
        double y,
        double speed,
        double power,
        double direction,
        int targetId,
        int type
    )
    {
        myBullets.Add(
            new MyVirtualBullet(
                x + 2 * speed * Math.Cos(direction),
                y + 2 * speed * Math.Sin(direction),
                speed,
                direction,
                power,
                targetId,
                type
            )
        );
    }

    // =========================
    // Helpers
    // =========================

    private EnemyInfo GetEnemy(int id)
    {
        if (!enemies.ContainsKey(id))
        {
            enemies[id] = new EnemyInfo();
        }

        return enemies[id];
    }

    private bool IsOutsideArena(double x, double y)
    {
        return
            x < -BULLET_BOUNDARY ||
            x > ArenaWidth + BULLET_BOUNDARY ||
            y < -BULLET_BOUNDARY ||
            y > ArenaHeight + BULLET_BOUNDARY;
    }

    private bool IsNearWall(double x, double y)
    {
        return
            x < WALL_MARGIN ||
            x > ArenaWidth - WALL_MARGIN ||
            y < WALL_MARGIN ||
            y > ArenaHeight - WALL_MARGIN;
    }

    private double DistanceSquared(
        double x1,
        double y1,
        double x2,
        double y2
    )
    {
        return
            Math.Pow(x1 - x2, 2) +
            Math.Pow(y1 - y2, 2);
    }

    private double Distance(
        double x1,
        double y1,
        double x2,
        double y2
    )
    {
        return Math.Sqrt(
            DistanceSquared(x1, y1, x2, y2)
        );
    }
}

// =========================
// State
// =========================

public struct State
{
    public int AngularVelocity;
    public int Speed;
    public int Acceleration;

    public State(
        double angularVelocity,
        double speed,
        double acceleration
    )
    {
        AngularVelocity =
            (int)(angularVelocity * 1024);

        Speed =
            (int)Math.Round(speed);

        const double threshold = 0.1;

        if (acceleration < -threshold)
            Acceleration = -1;
        else if (acceleration > threshold)
            Acceleration = 1;
        else
            Acceleration = 0;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is State state))
            return false;

        return
            state.AngularVelocity == AngularVelocity &&
            state.Speed == Speed &&
            state.Acceleration == Acceleration;
    }

    public override int GetHashCode()
    {
        return
            AngularVelocity.GetHashCode() ^
            Speed.GetHashCode() ^
            Acceleration.GetHashCode();
    }
}

// =========================
// State Sequence
// =========================

public class StateSequence
{
    public List<State> States { get; }

    public StateSequence(IEnumerable<State> states)
    {
        States = new List<State>(states);
    }

    public override bool Equals(object obj)
    {
        if (!(obj is StateSequence other))
            return false;

        if (States.Count != other.States.Count)
            return false;

        for (int i = 0; i < States.Count; i++)
        {
            if (!States[i].Equals(other.States[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        int hash = 17;

        foreach (State state in States)
        {
            hash = hash * 31 + state.GetHashCode();
        }

        return hash;
    }
}

// =========================
// Enemy Info
// =========================

public class EnemyInfo
{
    public List<State> StateHistory { get; } =
        new List<State>();

    public Dictionary<StateSequence, FrequencyTree>
        NgramTree { get; } =
        new Dictionary<StateSequence, FrequencyTree>();

    public List<int> AimModes { get; set; } =
        new List<int> { 5, 0 };

    public bool HasPrevious { get; set; }
    public bool IsAlive { get; set; } = true;

    public double LastDirection { get; set; }
    public double LastX { get; set; }
    public double LastY { get; set; }
    public double LastEnergy { get; set; }
    public double LastSpeed { get; set; }
}

// =========================
// Bullet
// =========================

public struct VirtualBullet
{
    public double X;
    public double Y;
    public double Speed;
    public double Direction;
    public double Power;
}

// =========================
// My Bullet
// =========================

public class MyVirtualBullet
{
    public VirtualBullet Data;

    public int TargetId;
    public int Type;

    public MyVirtualBullet(
        double x,
        double y,
        double speed,
        double direction,
        double power,
        int targetId,
        int type
    )
    {
        Data = new VirtualBullet
        {
            X = x,
            Y = y,
            Speed = speed,
            Direction = direction,
            Power = power
        };

        TargetId = targetId;
        Type = type;
    }
}

// =========================
// Line2D
// =========================

public class Line2D
{
    public double X1 { get; }
    public double Y1 { get; }

    public double X2 { get; }
    public double Y2 { get; }

    public Line2D(
        double x1,
        double y1,
        double x2,
        double y2
    )
    {
        X1 = x1;
        Y1 = y1;

        X2 = x2;
        Y2 = y2;
    }

    public double DistanceToPoint(
        double px,
        double py
    )
    {
        return Math.Abs(
            (Y2 - Y1) * px -
            (X2 - X1) * py +
            (X2 * Y1 - Y2 * X1)
        )
        /
        Math.Sqrt(
            Math.Pow(Y2 - Y1, 2) +
            Math.Pow(X2 - X1, 2)
        );
    }
}

// =========================
// Frequency Tree
// =========================

public class FrequencyTree
{
    private List<KeyValuePair<State, int>> data;

    private int size;

    private (State state, int count)[] tree;

    private Dictionary<State, int> indexMap;

    public FrequencyTree()
    {
        data =
            new List<KeyValuePair<State, int>>();

        indexMap =
            new Dictionary<State, int>();

        tree =
            new (State, int)[0];
    }

    public void Add(State state)
    {
        if (indexMap.ContainsKey(state))
        {
            int index = indexMap[state];

            var entry = data[index];

            data[index] =
                new KeyValuePair<State, int>(
                    state,
                    entry.Value + 1
                );
        }
        else
        {
            indexMap[state] = data.Count;

            data.Add(
                new KeyValuePair<State, int>(
                    state,
                    1
                )
            );
        }

        Rebuild();
    }

    private void Rebuild()
    {
        int n = data.Count;

        if (n == 0)
        {
            tree = new (State, int)[0];
            size = 0;
            return;
        }

        size = 1;

        while (size < n)
            size *= 2;

        tree =
            new (State, int)[2 * size];

        for (int i = 0; i < size; i++)
        {
            if (i < n)
            {
                tree[size + i] =
                    (data[i].Key, data[i].Value);
            }
            else
            {
                tree[size + i] =
                    (default(State), 0);
            }
        }

        for (int i = size - 1; i > 0; i--)
        {
            var left = tree[i * 2];
            var right = tree[i * 2 + 1];

            tree[i] =
                left.count >= right.count
                ? left
                : right;
        }
    }

    public State GetMostFrequent()
    {
        if (tree.Length == 0)
            return default(State);

        return tree[1].state;
    }
}