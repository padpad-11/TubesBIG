using System;
using System.Linq;
using Robocode.TankRoyale.BotApi.Graphics;
using Color = Robocode.TankRoyale.BotApi.Graphics.Color;
using System.Collections.Generic;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;


public class DPlusKIW : Bot
{
    // ===== Constants =====
    private const double WALL_SPACE = 30;
    private const double SAFE_CORNER_SPACE = 60;
    private const double STICK_SIZE = 100;
    private const double ORBIT_RADIUS = 60;
    private const double ORBIT_SPEED = 8;
    private const double MAX_MOVE_SPEED = 8;

    private double FIRE_SCALE = 5;

    // ===== Runtime Data =====
    private Dictionary<int, TargetInfo> scannedEnemies = new Dictionary<int, TargetInfo>();

    private Random rng = new Random();

    private Vector2 safeCorner = new Vector2(SAFE_CORNER_SPACE, SAFE_CORNER_SPACE);

    private bool movingToCorner;
    private bool duelMode;
    private bool radarLocked;
    private bool scanningEveryone;

    private int focusedEnemy;

    private Queue<Vector2> movementTrail = new Queue<Vector2>();

    static void Main()
    {
        new DPlusKIW().Start();
    }

    DPlusKIW() : base(BotInfo.FromFile("DPlusKIW.json")) { }

    // ============================================================
    public override void Run()
    {
        Console.WriteLine("DPlusKIW online.");

        BodyColor = Color.Blue;
        TurretColor = Color.Blue;
        RadarColor = Color.Orange;
        BulletColor = Color.Red;
        ScanColor = Color.Yellow;

        AdjustGunForBodyTurn = true;
        AdjustRadarForGunTurn = true;
        AdjustRadarForBodyTurn = true;

        scannedEnemies = new Dictionary<int, TargetInfo>();

        MaxSpeed = MAX_MOVE_SPEED;

        movingToCorner = true;
        duelMode = false;
        radarLocked = false;
        scanningEveryone = true;

        focusedEnemy = -1;

        for (int i = 0; i < 8; i++)
        {
            TurnRadarRight(45);
        }

        scanningEveryone = false;

        safeCorner = FindSafeCorner();
    }

    // ============================================================
    public override void OnTick(TickEvent e)
    {
        movementTrail.Enqueue(new Vector2(X, Y));

        if (movementTrail.Count > 50)
        {
            movementTrail.Dequeue();
        }

        HandleMovement();

        HandleTargeting();
    }

    // ============================================================
    private void HandleMovement()
    {
        if (movingToCorner && !AvoidWalls())
        {
            double curve =
                40 * Math.Sin(TurnNumber * 2 * Math.PI / 40);

            double heading =
                BearingTo(safeCorner.X, safeCorner.Y);

            Vector2 destination =
                ProjectPoint(heading + curve, Direction);

            TravelTo(destination.X, destination.Y);

            if (DistanceTo(safeCorner.X, safeCorner.Y) < ORBIT_RADIUS)
            {
                TargetSpeed = 0;
                movingToCorner = false;
            }
        }
        else
        {
            OrbitMove();
        }

        if (DistanceTo(safeCorner.X, safeCorner.Y) > ORBIT_RADIUS * 4)
        {
            movingToCorner = true;
        }

        Vector2 nextCorner = FindSafeCorner();

        if (!nextCorner.Equals(safeCorner))
        {
            safeCorner = nextCorner;
            movingToCorner = true;
        }
    }

    // ============================================================
    private void HandleTargeting()
    {
        if (!duelMode)
        {
            SetTurnRadarLeft(20);

            int enemyId = PickEnemy();

            if (enemyId != -1)
            {
                PredictiveAim(
                    enemyId,
                    CalculateFirePower(
                        scannedEnemies[enemyId].LastX,
                        scannedEnemies[enemyId].LastY
                    )
                );
            }

            return;
        }

        if (radarLocked)
        {
            LockRadarAt(
                scannedEnemies[focusedEnemy].LastX,
                scannedEnemies[focusedEnemy].LastY
            );

            PredictiveAim(
                focusedEnemy,
                CalculateFirePower(
                    scannedEnemies[focusedEnemy].LastX,
                    scannedEnemies[focusedEnemy].LastY
                )
            );

            radarLocked = false;
        }
        else
        {
            SetTurnRadarLeft(20);
        }
    }

    // ============================================================
    public override void OnScannedBot(ScannedBotEvent e)
    {
        if (!scannedEnemies.ContainsKey(e.ScannedBotId))
        {
            scannedEnemies[e.ScannedBotId] = new TargetInfo();
        }

        TargetInfo enemy = scannedEnemies[e.ScannedBotId];

        enemy.PrevX = enemy.LastX;
        enemy.PrevY = enemy.LastY;
        enemy.PrevDirection = enemy.LastDirection;
        enemy.PrevSpeed = enemy.LastSpeed;
        enemy.PrevTick = enemy.LastTick;

        enemy.LastX = e.X;
        enemy.LastY = e.Y;
        enemy.LastSpeed = e.Speed;
        enemy.LastDirection = e.Direction;
        enemy.LastEnergy = e.Energy;
        enemy.LastTick = TurnNumber;

        if ((DistanceTo(e.X, e.Y) < 200 || scannedEnemies.Count == 1)
            && !scanningEveryone)
        {
            focusedEnemy = e.ScannedBotId;
            duelMode = true;
        }
        else
        {
            duelMode = false;
        }

        if (e.ScannedBotId == focusedEnemy)
        {
            radarLocked = true;
        }
    }

    // ============================================================
    public override void OnBotDeath(BotDeathEvent e)
    {
        if (!scannedEnemies.ContainsKey(e.VictimId))
            return;

        scannedEnemies.Remove(e.VictimId);

        if (focusedEnemy == e.VictimId)
        {
            duelMode = false;
            focusedEnemy = -1;
        }

        if (scannedEnemies.Count == 1)
        {
            focusedEnemy = scannedEnemies.Keys.First();
            duelMode = true;
        }
    }

    // ============================================================
    private void OrbitMove()
    {
        if (AvoidWalls())
            return;

        double offset =
            20 + 40 * Math.Sin(TurnNumber * 2 * Math.PI / 45);

        Vector2 next =
            ProjectPoint(offset, Direction);

        double px =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaWidth - WALL_SPACE, next.X));

        double py =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaHeight - WALL_SPACE, next.Y));

        TravelTo(px, py, ORBIT_SPEED);
    }

    // ============================================================
    private bool AvoidWalls()
    {
        Vector2 front =
            ProjectPoint(0, Direction);

        if (!OutsideArena(front.X, front.Y))
            return false;

        front.X =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaWidth - WALL_SPACE, front.X));

        front.Y =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaHeight - WALL_SPACE, front.Y));

        var (left, right) = BuildWallSticks();

        double leftAngle =
            Math.Abs(BearingTo(left.X, left.Y)
            - BearingTo(front.X, front.Y));

        double rightAngle =
            Math.Abs(BearingTo(right.X, right.Y)
            - BearingTo(front.X, front.Y));

        if (DistanceTo(left.X, left.Y) < STICK_SIZE &&
            DistanceTo(right.X, right.Y) < STICK_SIZE)
        {
            TravelTo(ArenaWidth / 2, ArenaHeight / 2);
            return true;
        }

        if (leftAngle < rightAngle)
        {
            TravelTo(left.X, left.Y);
        }
        else
        {
            TravelTo(right.X, right.Y);
        }

        return true;
    }

    // ============================================================
    private Vector2 FindSafeCorner()
    {
        Vector2[] corners =
        {
            new Vector2(SAFE_CORNER_SPACE, SAFE_CORNER_SPACE),
            new Vector2(SAFE_CORNER_SPACE, ArenaHeight - SAFE_CORNER_SPACE),
            new Vector2(ArenaWidth - SAFE_CORNER_SPACE, SAFE_CORNER_SPACE),
            new Vector2(ArenaWidth - SAFE_CORNER_SPACE, ArenaHeight - SAFE_CORNER_SPACE)
        };

        Vector2 safest = corners[0];

        double highestAverage = 0;

        foreach (Vector2 c in corners)
        {
            double total = 0;

            foreach (TargetInfo enemy in scannedEnemies.Values)
            {
                total += Math.Sqrt(
                    Math.Pow(c.X - enemy.LastX, 2) +
                    Math.Pow(c.Y - enemy.LastY, 2)
                );
            }

            double average =
                total / (scannedEnemies.Count > 0 ? scannedEnemies.Count : 1);

            if (average > highestAverage)
            {
                highestAverage = average;
                safest = c;
            }
        }

        return safest;
    }

    // ============================================================
    private void TravelTo(double x, double y, double speed = 8)
    {
        double turn =
            speed > 0
            ? BearingTo(x, y)
            : 180 - BearingTo(x, y);

        SetTurnLeft(turn);

        double distance = DistanceTo(x, y);

        if (Math.Abs(turn) < 30 && distance < STICK_SIZE)
        {
            TargetSpeed = speed * distance / STICK_SIZE;
        }
        else
        {
            TargetSpeed = Math.Abs(speed);
        }
    }

    // ============================================================
    private void LockRadarAt(double x, double y)
    {
        double offset =
            NormalizeRelativeAngle(RadarBearingTo(x, y));

        SetTurnRadarLeft(offset + (offset > 0 ? 20 : -20));
    }

    // ============================================================
    private int PickEnemy()
    {
        double closest = double.PositiveInfinity;
        int id = -1;

        foreach (int enemyId in scannedEnemies.Keys)
        {
            double distance =
                DistanceTo(
                    scannedEnemies[enemyId].LastX,
                    scannedEnemies[enemyId].LastY
                );

            if (distance < closest)
            {
                closest = distance;
                id = enemyId;
            }
        }

        return id;
    }

    // ============================================================
    private double CalculateFirePower(double x, double y)
    {
        return Energy / DistanceTo(x, y) * FIRE_SCALE;
    }

    // ============================================================
    private void PredictiveAim(int enemyId, double firePower)
    {
        TargetInfo enemy = scannedEnemies[enemyId];

        if (enemy.LastX == enemy.PrevX &&
            enemy.LastY == enemy.PrevY)
        {
            DirectAim(enemy.LastX, enemy.LastY, firePower);
            return;
        }

        double bulletSpeed =
            CalcBulletSpeed(firePower);

        double deltaTime =
            enemy.LastTick - enemy.PrevTick;

        double turnRate =
            NormalizeRelativeAngle(
                enemy.LastDirection - enemy.PrevDirection
            ) / deltaTime;

        if (Math.Abs(turnRate) < 1)
        {
            PredictLinearShot(
                enemy.LastX,
                enemy.LastY,
                enemy.LastSpeed,
                enemy.LastDirection,
                firePower
            );

            return;
        }

        double radius =
            Math.Abs(enemy.LastSpeed /
            DegreesToRadians(turnRate));

        Vector2 center =
            ProjectPoint(
                turnRate > 0 ? 90 : -90,
                enemy.LastDirection,
                radius,
                new Vector2(enemy.LastX, enemy.LastY)
            );

        double travelTime =
            DistanceTo(center.X, center.Y) / bulletSpeed;

        double theta =
            turnRate * travelTime;

        Vector2 future =
            RotateAround(
                new Vector2(enemy.LastX, enemy.LastY),
                center,
                theta
            );

        double gunTurn =
            GunBearingTo(future.X, future.Y);

        SetTurnGunLeft(gunTurn);

        SetFire(firePower);
    }

    // ============================================================
    private void PredictLinearShot(
        double tx,
        double ty,
        double velocity,
        double heading,
        double power)
    {
        double bulletSpeed = CalcBulletSpeed(power);

        double vx =
            velocity * Math.Cos(DegreesToRadians(heading));

        double vy =
            velocity * Math.Sin(DegreesToRadians(heading));

        double a =
            Math.Pow(vx, 2) +
            Math.Pow(vy, 2) -
            Math.Pow(bulletSpeed, 2);

        double b =
            2 * (vx * (tx - X) + vy * (ty - Y));

        double c =
            Math.Pow(tx - X, 2) +
            Math.Pow(ty - Y, 2);

        double determinant =
            Math.Pow(b, 2) - 4 * a * c;

        if (determinant < 0)
        {
            DirectAim(tx, ty, power);
            return;
        }

        double t1 =
            (-b + Math.Sqrt(determinant)) / (2 * a);

        double t2 =
            (-b - Math.Sqrt(determinant)) / (2 * a);

        double impactTime =
            Math.Min(
                t1 > 0 ? t1 : double.PositiveInfinity,
                t2 > 0 ? t2 : double.PositiveInfinity
            );

        double futureX =
            tx + velocity *
            impactTime *
            Math.Cos(DegreesToRadians(heading));

        double futureY =
            ty + velocity *
            impactTime *
            Math.Sin(DegreesToRadians(heading));

        double gunAngle =
            GunBearingTo(futureX, futureY);

        SetTurnGunLeft(gunAngle);

        SetFire(power);
    }

    // ============================================================
    private void DirectAim(double x, double y, double power)
    {
        double gunTurn = GunBearingTo(x, y);

        SetTurnGunLeft(gunTurn);

        SetFire(power);
    }

    // ============================================================
    private (Vector2, Vector2) BuildWallSticks()
    {
        Vector2 left =
            ProjectPoint(90, Direction);

        Vector2 right =
            ProjectPoint(-90, Direction);

        left.X =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaWidth - WALL_SPACE, left.X));

        left.Y =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaHeight - WALL_SPACE, left.Y));

        right.X =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaWidth - WALL_SPACE, right.X));

        right.Y =
            Math.Max(WALL_SPACE,
            Math.Min(ArenaHeight - WALL_SPACE, right.Y));

        return (left, right);
    }

    // ============================================================
    private Vector2 ProjectPoint(
        double angle,
        double heading,
        double distance = STICK_SIZE,
        Vector2 origin = null)
    {
        if (origin == null)
        {
            origin = new Vector2(X, Y);
        }

        double px =
            origin.X +
            distance *
            Math.Cos(DegreesToRadians(heading + angle));

        double py =
            origin.Y +
            distance *
            Math.Sin(DegreesToRadians(heading + angle));

        return new Vector2(px, py);
    }

    // ============================================================
    private bool OutsideArena(double x, double y)
    {
        return x < 0 ||
               x > ArenaWidth ||
               y < 0 ||
               y > ArenaHeight;
    }

    private double DegreesToRadians(double degree)
    {
        return degree * Math.PI / 180;
    }

    private Vector2 RotateAround(
        Vector2 point,
        Vector2 center,
        double angle)
    {
        double rx =
            center.X +
            (point.X - center.X) * Math.Cos(DegreesToRadians(angle)) -
            (point.Y - center.Y) * Math.Sin(DegreesToRadians(angle));

        double ry =
            center.Y +
            (point.X - center.X) * Math.Sin(DegreesToRadians(angle)) +
            (point.Y - center.Y) * Math.Cos(DegreesToRadians(angle));

        return new Vector2(rx, ry);
    }
}

// ================================================================

class TargetInfo
{
    public double LastX { get; set; }
    public double LastY { get; set; }
    public double LastDirection { get; set; }
    public double LastSpeed { get; set; }
    public double LastEnergy { get; set; }
    public double LastTick { get; set; }

    public double PrevX { get; set; }
    public double PrevY { get; set; }
    public double PrevDirection { get; set; }
    public double PrevSpeed { get; set; }
    public double PrevTick { get; set; }
}

class Vector2
{
    public double X;
    public double Y;

    public Vector2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(Vector2 other)
    {
        return X == other.X && Y == other.Y;
    }
}