using System;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;
using Robocode.TankRoyale.BotApi.Graphics;
using Color = Robocode.TankRoyale.BotApi.Graphics.Color;

public class Alpha6 : Bot
{
    private double arenaH = 600;
    private double arenaW = 800;

    private double midX = 400;
    private double midY = 300;

    private double wallLimit = 100;

    private double enemyX;
    private double enemyY;
    private double enemyVelocity;
    private double enemyHeading;
    private double enemyEnergyNow;

    private bool enemySeen = false;

    private double bulletPower;

    private const double RADAR_FACTOR = 0.7;
    private static double FIRE_RATIO = 5;

    private static BotEnemy enemyInfo;

    static void Main()
    {
        new Alpha6().Start();
    }

    Alpha6() : base(BotInfo.FromFile("Alpha6.json")) { }

    public override void Run()
    {
        BodyColor = Color.Green;
        TurretColor = Color.DarkGreen;
        RadarColor = Color.Red;
        BulletColor = Color.Brown;
        ScanColor = Color.Yellow;
        TracksColor = Color.Brown;
        GunColor = Color.Green;

        AdjustGunForBodyTurn = true;
        AdjustRadarForGunTurn = true;
        AdjustRadarForBodyTurn = true;

        SetTurnRadarRight(double.PositiveInfinity);
    }

    public override void OnTick(TickEvent e)
    {
        if (!enemySeen) return;

        enemySeen = false;

        if (NearWall())
        {
            ReturnCenter();
        }
        else
        {
            RepelMovement();
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        enemyX = e.X;
        enemyY = e.Y;
        enemyVelocity = e.Speed;
        enemyHeading = e.Direction;
        enemyEnergyNow = e.Energy;

        enemySeen = true;

        double bearingEnemy = EnemyBearing(enemyX, enemyY);

        enemyInfo = new BotEnemy(
            enemyX + DistanceTo(enemyX, enemyY) * Math.Sin(bearingEnemy),
            enemyY + DistanceTo(enemyX, enemyY) * Math.Cos(bearingEnemy),
            enemyHeading,
            enemyVelocity,
            enemyEnergyNow
        );

        RadarTracking(enemyX, enemyY);

        ConfigureFire(enemyX, enemyY);

        PredictiveShot(enemyX, enemyY, enemyVelocity, enemyHeading, bulletPower);
    }

    // ========================== UTIL ==========================

    public double EnemyBearing(double ex, double ey)
    {
        double dx = ex - X;
        double dy = ey - Y;

        return Math.Atan2(dy, dx) + (Direction * (Math.PI / 180));
    }

    public bool NearWall()
    {
        return (
            X < wallLimit ||
            X > arenaW - wallLimit ||
            Y < wallLimit ||
            Y > arenaH - wallLimit
        );
    }

    public void RadarTracking(double ex, double ey)
    {
        double radarMove =
            double.PositiveInfinity *
            NormalizeRelativeAngle(RadarBearingTo(ex, ey));

        if (!double.IsNaN(radarMove) &&
            (GunHeat < RADAR_FACTOR || EnemyCount == 1))
        {
            SetTurnRadarLeft(radarMove);
        }
    }

    public void ConfigureFire(double ex, double ey)
    {
        bulletPower = Energy / DistanceTo(ex, ey) * FIRE_RATIO;

        if (GunTurnRemaining == 0)
        {
            SetFire(bulletPower);
        }
    }

    public double EnemyDistance(BotEnemy data)
    {
        return DistanceTo(data.pos.X, data.pos.Y);
    }

    public void PredictiveShot(
        double tx,
        double ty,
        double speed,
        double heading,
        double firePow
    )
    {
        double bulletVel = CalcBulletSpeed(firePow);

        double vx = speed * Math.Cos(ToRadians(heading));
        double vy = speed * Math.Sin(ToRadians(heading));

        double a =
            Math.Pow(vx, 2) +
            Math.Pow(vy, 2) -
            Math.Pow(bulletVel, 2);

        double b =
            2 * (
                vx * (tx - X) +
                vy * (ty - Y)
            );

        double c =
            Math.Pow(tx - X, 2) +
            Math.Pow(ty - Y, 2);

        double delta = Math.Pow(b, 2) - 4 * a * c;

        if (delta < 0)
        {
            SetTurnGunLeft(GunBearingTo(tx, ty));
            SetFire(firePow);
            return;
        }

        double root1 = (-b + Math.Sqrt(delta)) / (2 * a);
        double root2 = (-b - Math.Sqrt(delta)) / (2 * a);

        double time =
            Math.Min(
                root1 > 0 ? root1 : double.PositiveInfinity,
                root2 > 0 ? root2 : double.PositiveInfinity
            );

        double futureX =
            tx + speed * time * Math.Cos(ToRadians(heading));

        double futureY =
            ty + speed * time * Math.Sin(ToRadians(heading));

        SetTurnGunLeft(GunBearingTo(futureX, futureY));

        SetFire(firePow);
    }

    public void ReturnCenter(double velocity = 8)
    {
        double rotate =
            velocity > 0
            ? BearingTo(midX, midY)
            : 180 - BearingTo(midX, midY);

        velocity = Math.Abs(velocity);

        SetTurnLeft(rotate);

        double radius =
            Math.Abs(
                (180 - Math.Abs(rotate)) /
                180 *
                velocity /
                (TurnRate == 0 ? 1 : TurnRate)
            );

        double dist = DistanceTo(midX, midY);

        if (Math.Abs(rotate) < 30 && dist < wallLimit)
        {
            TargetSpeed = velocity * dist / wallLimit;
        }
        else
        {
            TargetSpeed =
                Math.Abs(
                    rotate != 0
                    ? TurnRate * radius
                    : velocity
                );
        }
    }

    public void RepelMovement()
    {
        double forceX = 0;
        double forceY = 0;

        double absoluteBearing =
            NormalizeAbsoluteAngle(
                ToDegrees(
                    Math.Atan2(enemyX - X, enemyY - Y)
                )
            );

        double distance = EnemyDistance(enemyInfo);

        forceX -=
            (Math.Sin(ToRadians(absoluteBearing)) /
            Math.Pow(distance, 2)) *
            enemyInfo.energy;

        forceY -=
            (Math.Cos(ToRadians(absoluteBearing)) /
            Math.Pow(distance, 2)) *
            enemyInfo.energy;

        double angle =
            NormalizeRelativeAngle(
                ToDegrees(Math.Atan2(forceX, forceY))
            );

        if (Math.Abs(CalcBearing(angle)) < 90)
        {
            SetTurnRight(CalcBearing(angle));
            SetForward(100);
        }
        else
        {
            SetTurnRight(
                CalcBearing(angle) > 0
                ? CalcBearing(angle) - 180
                : CalcBearing(angle) + 180
            );

            Back(50);
        }
    }

    public double ToRadians(double deg)
    {
        return deg * (Math.PI / 180);
    }

    public double ToDegrees(double rad)
    {
        return rad * (180 / Math.PI);
    }
}

public struct BotEnemy
{
    public Vec2 pos;
    public double direction;
    public double velocity;
    public double energy;

    public BotEnemy(
        double x,
        double y,
        double direction,
        double velocity,
        double energy
    )
    {
        pos = new Vec2(x, y);
        this.direction = direction;
        this.velocity = velocity;
        this.energy = energy;
    }

    public BotEnemy(
        Vec2 pos,
        double direction,
        double velocity,
        double energy
    )
    {
        this.pos = pos;
        this.direction = direction;
        this.velocity = velocity;
        this.energy = energy;
    }
}

public struct Vec2
{
    public double X;
    public double Y;

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }
}