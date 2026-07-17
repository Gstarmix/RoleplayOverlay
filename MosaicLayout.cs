using System;
using System.Collections.Generic;
using System.Linq;
namespace RoleplayOverlay
{
  public readonly struct CellRect
  {
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public CellRect(int x, int y, int w, int h)
    {
      X = x; Y = y; Width = w; Height = h;
    }
  }
  public static class MosaicLayout
  {
    public static List<CellRect> GetCells(int count, int totalW, int totalH, int gap)
    {
      if (count <= 0) return new List<CellRect>();
      count = Math.Min(count, 6);
      var cells = new List<CellRect>(count);
      switch (count)
      {
        case 1:
          cells.Add(new CellRect(0, 0, totalW, totalH));
          break;
        case 2:
        {
          int halfW = (totalW - gap) / 2;
          cells.Add(new CellRect(0, 0, halfW, totalH));
          cells.Add(new CellRect(halfW + gap, 0, totalW - halfW - gap, totalH));
          break;
        }
        case 3:
        {
          int leftW = (totalW - gap) / 2;
          int rightW = totalW - leftW - gap;
          int halfH = (totalH - gap) / 2;
          cells.Add(new CellRect(0, 0, leftW, totalH));
          cells.Add(new CellRect(leftW + gap, 0, rightW, halfH));
          cells.Add(new CellRect(leftW + gap, halfH + gap, rightW, totalH - halfH - gap));
          break;
        }
        case 4:
        {
          int halfW = (totalW - gap) / 2;
          int halfH = (totalH - gap) / 2;
          int rightW = totalW - halfW - gap;
          int bottomH = totalH - halfH - gap;
          cells.Add(new CellRect(0, 0, halfW, halfH));
          cells.Add(new CellRect(halfW + gap, 0, rightW, halfH));
          cells.Add(new CellRect(0, halfH + gap, halfW, bottomH));
          cells.Add(new CellRect(halfW + gap, halfH + gap, rightW, bottomH));
          break;
        }
        case 5:
        {
          int halfW = (totalW - gap) / 2;
          int halfH = (totalH - gap) / 2;
          int bottomH = totalH - halfH - gap;
          int thirdW = (totalW - 2 * gap) / 3;
          int lastW = totalW - 2 * thirdW - 2 * gap;
          cells.Add(new CellRect(0, 0, halfW, halfH));
          cells.Add(new CellRect(halfW + gap, 0, totalW - halfW - gap, halfH));
          cells.Add(new CellRect(0, halfH + gap, thirdW, bottomH));
          cells.Add(new CellRect(thirdW + gap, halfH + gap, thirdW, bottomH));
          cells.Add(new CellRect(2 * (thirdW + gap), halfH + gap, lastW, bottomH));
          break;
        }
        case 6:
        {
          int thirdW = (totalW - 2 * gap) / 3;
          int halfH = (totalH - gap) / 2;
          int lastW = totalW - 2 * thirdW - 2 * gap;
          int bottomH = totalH - halfH - gap;
          for (int row = 0; row < 2; row++)
          {
            int y = row == 0 ? 0 : halfH + gap;
            int h = row == 0 ? halfH : bottomH;
            cells.Add(new CellRect(0, y, thirdW, h));
            cells.Add(new CellRect(thirdW + gap, y, thirdW, h));
            cells.Add(new CellRect(2 * (thirdW + gap), y, lastW, h));
          }
          break;
        }
      }
      return cells;
    }
    public static string ToXStackLayoutString(List<CellRect> cells)
    {
      return string.Join("|", cells.Select(c => $"{c.X}_{c.Y}"));
    }
  }
}