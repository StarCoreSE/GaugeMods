using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
	public class MyFreeList<TItem>
	{
		public TItem[] ItemArray;

		private int m_size;

		private readonly Queue<int> m_freePositions;

		private readonly TItem m_default;

		/// <summary>
		/// Length of the internal buffer occupied by a mix of free and empty nodes, data past this length is all free.
		/// </summary>
		public int UsedLength => m_size;

		/// <summary>
		/// Total number of allocated positions.
		/// </summary>
		public int Count => m_size - m_freePositions.Count;

		public int Capacity => ItemArray.Length;

		public MyFreeList(int capacity = 16, TItem defaultValue = default(TItem))
		{
			ItemArray = new TItem[16];
			m_freePositions = new Queue<int>(capacity / 2);
			m_default = defaultValue;
		}

		public int Allocate()
		{
			if (m_freePositions.Count > 0)
			{
				return m_freePositions.Dequeue();
			}
			if (m_size == ItemArray.Length)
			{
				Array.Resize(ref ItemArray, ItemArray.Length << 1);
			}
			return m_size++;
		}

		public int Allocate(TItem value)
		{
			int num = Allocate();
			ItemArray[num] = value;
			return num;
		}

		public void Free(int position)
		{
			ItemArray[position] = m_default;
			if (position == m_size)
			{
				m_size--;
			}
			else
			{
				m_freePositions.Enqueue(position);
			}
		}

		public TItem[] GetInternalArray()
		{
			return ItemArray;
		}

		public bool KeyValid(int key)
		{
			return (uint)key < m_size;
		}

		public void Clear()
		{
			for (int i = 0; i < m_size; i++)
			{
				ItemArray[i] = default(TItem);
			}
			m_size = 0;
			m_freePositions.Clear();
		}
	}

	/// <summary>
	/// Optimized thermal cell array for better cache performance and parallel processing
	/// </summary>
	public class ThermalCellArray
	{
		private ThermalCell[] cells;
		private int count;
		private readonly Dictionary<int, int> positionToIndex = new Dictionary<int, int>();
		private readonly List<int> freeIndices = new List<int>();

		public int Count => count;
		public ThermalCell[] Cells => cells;

		public ThermalCellArray(int initialCapacity = 1024)
		{
			cells = new ThermalCell[initialCapacity];
		}

		public int Add(ThermalCell cell)
		{
			int index;
			if (freeIndices.Count > 0)
			{
				index = freeIndices[freeIndices.Count - 1];
				freeIndices.RemoveAt(freeIndices.Count - 1);
			}
			else
			{
				if (count >= cells.Length)
				{
					Array.Resize(ref cells, cells.Length * 2);
				}
				index = count++;
			}

			cells[index] = cell;
			positionToIndex[cell.Id] = index;
			return index;
		}

		public void Remove(int positionId)
		{
			int index;
			if (positionToIndex.TryGetValue(positionId, out index))
			{
				cells[index] = null;
				positionToIndex.Remove(positionId);
				freeIndices.Add(index);
			}
		}

		public ThermalCell GetByPosition(int positionId)
		{
			int index;
			if (positionToIndex.TryGetValue(positionId, out index))
			{
				return cells[index];
			}
			return null;
		}

		public ThermalCell GetByIndex(int index)
		{
			if (index >= 0 && index < cells.Length)
			{
				return cells[index];
			}
			return null;
		}

		public void Compact()
		{
			// Move all non-null cells to the beginning of the array
			int writeIndex = 0;
			positionToIndex.Clear();
			freeIndices.Clear();

			for (int readIndex = 0; readIndex < cells.Length; readIndex++)
			{
				if (cells[readIndex] != null)
				{
					if (readIndex != writeIndex)
					{
						cells[writeIndex] = cells[readIndex];
						cells[readIndex] = null;
					}
					positionToIndex[cells[writeIndex].Id] = writeIndex;
					writeIndex++;
				}
			}

			count = writeIndex;

			// Shrink array if too much wasted space
			if (cells.Length > count * 2 && count > 0)
			{
				Array.Resize(ref cells, Math.Max(count * 2, 1024));
			}
		}

		public void Clear()
		{
			Array.Clear(cells, 0, cells.Length);
			count = 0;
			positionToIndex.Clear();
			freeIndices.Clear();
		}
	}
}
