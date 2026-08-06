using System.Drawing;

namespace AutoFlow.Core.Models;

public readonly record struct TemplateMatchInfo(
    int X,
    int Y,
    int Width,
    int Height,
    double Score,
    double Threshold)
{
    public bool IsMatch => Score >= Threshold;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public Point TopLeft => new(X, Y);

    public Point Center => new(X + Width / 2, Y + Height / 2);

    public Rectangle Bounds => new(X, Y, Width, Height);
}
