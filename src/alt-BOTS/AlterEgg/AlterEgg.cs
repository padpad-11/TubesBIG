using System;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;
using Robocode.TankRoyale.BotApi.Graphics;
public class AlterEgg : Bot
{
    static void Main(string[] args)
    {
        new AlterEgg().Start();
    }

    public AlterEgg() : base(BotInfo.FromFile("AlterEgg.json")) { }

    public override void Run()
    {
        AdjustRadarForBodyTurn = true;
        AdjustGunForBodyTurn = true;

        BodyColor = Color.Gray;
        TurretColor = Color.Cyan;
        RadarColor = Color.Green;
        BulletColor = Color.Yellow;
        TracksColor = Color.DarkGray;
        ScanColor = Color.Magenta;

        while (IsRunning)
        {
            TurnRadarRight(double.PositiveInfinity);
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        LockRadar(e);

        double distance = DistanceTo(e.X, e.Y);
        double power = Math.Max((Energy * 3) / distance, 0.1);

        PredictiveAim(
            e.X,
            e.Y,
            e.Speed,
            e.Direction,
            power
        );

        if (GunTurnRemaining == 0)
        {
            Fire(power);
        }

        MoveUsingRisk(e);
    }

    private void LockRadar(ScannedBotEvent e)
    {
        double radarTurn =
            NormalizeRelativeAngle(RadarBearingTo(e.X, e.Y));

        SetTurnRadarLeft(radarTurn * double.PositiveInfinity);
    }

    private void MoveUsingRisk(ScannedBotEvent enemy)
    {
        double bestRisk = 0;

        double moveX = enemy.X;
        double moveY = enemy.Y;

        for (int angle = 0; angle < 360; angle++)
        {
            double px =
                X + 100 * Math.Cos(ToRadians(angle));

            double py =
                Y + 100 * Math.Sin(ToRadians(angle));

            double danger =
                enemy.Energy /
                (Math.Pow(px - enemy.X, 2) +
                 Math.Pow(py - enemy.Y, 2) + 0.000001);

            if (danger > bestRisk)
            {
                bestRisk = danger;
                moveX = px;
                moveY = py;
            }
        }

        double heading =
            BearingTo(moveX, moveY) * Math.PI / 180;

        SetTurnLeft(
            Math.Tan(heading) * 180 / Math.PI
        );

        SetForward(
            DistanceTo(moveX, moveY) * Math.Cos(heading)
        );
    }

    private void PredictiveAim(
        double enemyX,
        double enemyY,
        double enemySpeed,
        double enemyHeading,
        double firePower)
    {
        double bulletSpeed = CalcBulletSpeed(firePower);

        double velocityX =
            enemySpeed * Math.Cos(ToRadians(enemyHeading));

        double velocityY =
            enemySpeed * Math.Sin(ToRadians(enemyHeading));

        double a =
            Math.Pow(velocityX, 2) +
            Math.Pow(velocityY, 2) -
            Math.Pow(bulletSpeed, 2);

        double b =
            2 * (
                velocityX * (enemyX - X) +
                velocityY * (enemyY - Y)
            );

        double c =
            Math.Pow(enemyX - X, 2) +
            Math.Pow(enemyY - Y, 2);

        double discriminant =
            Math.Pow(b, 2) - (4 * a * c);

        double root1 =
            (-b + Math.Sqrt(discriminant)) / (2 * a);

        double root2 =
            (-b - Math.Sqrt(discriminant)) / (2 * a);

        double hitTime =
            Math.Min(
                root1 > 0 ? root1 : double.PositiveInfinity,
                root2 > 0 ? root2 : double.PositiveInfinity
            );

        double futureX =
            enemyX +
            enemySpeed *
            hitTime *
            Math.Cos(ToRadians(enemyHeading));

        double futureY =
            enemyY +
            enemySpeed *
            hitTime *
            Math.Sin(ToRadians(enemyHeading));

        futureX =
            Math.Max(0, Math.Min(ArenaWidth, futureX));

        futureY =
            Math.Max(0, Math.Min(ArenaHeight, futureY));

        Graphics.DrawRectangle(
        (float)futureX,
        (float)futureY,
        20,
        20
        );

        double gunTurn =
            GunBearingTo(futureX, futureY);

        SetTurnGunLeft(gunTurn);
    }

    private double ToRadians(double angle)
    {
        return angle * Math.PI / 180;
    }
}