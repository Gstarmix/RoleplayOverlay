using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public sealed class UndoRedoService
  {
    private readonly int           _maxLevels;
    private readonly Stack<string> _undoStack  = new();
    private readonly Stack<string> _redoStack  = new();
    private readonly Stack<string> _undoLabels = new();
    private readonly Stack<string> _redoLabels = new();
    public event EventHandler? StateChanged;
    public bool CanUndo   => _undoStack.Count > 0;
    public bool CanRedo   => _redoStack.Count > 0;
    public int  UndoCount => _undoStack.Count;
    public int  RedoCount => _redoStack.Count;
    public IReadOnlyList<string> UndoLabels => _undoLabels.ToArray();
    public IReadOnlyList<string> RedoLabels => _redoLabels.ToArray();
    public UndoRedoService(int maxLevels = 50)
    {
      _maxLevels = Math.Max(5, maxLevels);
    }
    public void PushUndo(IEnumerable<Sequence> currentItems, string? label = null)
    {
      var snapshot = Serialize(currentItems);
      if (_undoStack.Count > 0 && _undoStack.Peek() == snapshot) return;
      _undoStack.Push(snapshot);
      _undoLabels.Push(label ?? "Modification");
      if (_undoStack.Count > _maxLevels)
      {
        TrimStack(_undoStack, _maxLevels);
        TrimStack(_undoLabels, _maxLevels);
      }
      _redoStack.Clear();
      _redoLabels.Clear();
      StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public List<Sequence>? Undo(IEnumerable<Sequence> currentItems)
    {
      if (_undoStack.Count == 0) return null;
      var currentSnapshot = Serialize(currentItems);
      var currentLabel    = _undoLabels.Count > 0 ? _undoLabels.Peek() : "État actuel";
      _redoStack.Push(currentSnapshot);
      _redoLabels.Push(currentLabel);
      var snapshot = _undoStack.Pop();
      if (_undoLabels.Count > 0) _undoLabels.Pop();
      var restored = Deserialize(snapshot);
      StateChanged?.Invoke(this, EventArgs.Empty);
      return restored;
    }
    public List<Sequence>? Redo(IEnumerable<Sequence> currentItems)
    {
      if (_redoStack.Count == 0) return null;
      var currentSnapshot = Serialize(currentItems);
      var currentLabel    = _redoLabels.Count > 0 ? _redoLabels.Peek() : "État actuel";
      _undoStack.Push(currentSnapshot);
      _undoLabels.Push(currentLabel);
      var snapshot = _redoStack.Pop();
      if (_redoLabels.Count > 0) _redoLabels.Pop();
      var restored = Deserialize(snapshot);
      StateChanged?.Invoke(this, EventArgs.Empty);
      return restored;
    }
    public List<Sequence>? UndoN(IEnumerable<Sequence> currentItems, int n)
    {
      var items = currentItems.ToList();
      List<Sequence>? result = null;
      for (int i = 0; i < n && CanUndo; i++)
        result = Undo(items = result ?? items);
      return result;
    }
    public List<Sequence>? RedoN(IEnumerable<Sequence> currentItems, int n)
    {
      var items = currentItems.ToList();
      List<Sequence>? result = null;
      for (int i = 0; i < n && CanRedo; i++)
        result = Redo(items = result ?? items);
      return result;
    }
    public void Clear()
    {
      _undoStack.Clear();  _undoLabels.Clear();
      _redoStack.Clear();  _redoLabels.Clear();
      StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private static string Serialize(IEnumerable<Sequence> items)
      => JsonConvert.SerializeObject(items.Select(s => s.Clone()).ToList());
    private static List<Sequence> Deserialize(string json)
      => JsonConvert.DeserializeObject<List<Sequence>>(json) ?? new List<Sequence>();
    private static void TrimStack<T>(Stack<T> stack, int maxSize)
    {
      if (stack.Count <= maxSize) return;
      var keep = stack.Take(maxSize).ToArray();
      stack.Clear();
      for (int i = keep.Length - 1; i >= 0; i--) stack.Push(keep[i]);
    }
  }
}