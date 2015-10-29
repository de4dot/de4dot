/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Text;
using System.Collections.Generic;

namespace de4dot.blocks.cflow {
	class ValueStack {
		List<Value> stack = new List<Value>();

		public int Size {
			get { return stack.Count; }
		}

		public void Initialize() {
			stack.Clear();
		}

		public void Clear() {
			stack.Clear();
		}

		public void Push(Value value) {
			stack.Add(value);
		}

		public Value Peek() {
			if (stack.Count == 0)
				return new UnknownValue();
			return stack[stack.Count - 1];
		}

		public Value Pop() {
			Value value = Peek();
			if (stack.Count != 0)
				stack.RemoveAt(stack.Count - 1);
			return value;
		}

		public void Push(int count) {
			if (count < 0)
				throw new ArgumentOutOfRangeException("count");
			for (int i = 0; i < count; i++)
				PushUnknown();
		}

		public void PushUnknown() {
			Push(new UnknownValue());
		}

		public void Pop(int count) {
			if (count < 0)
				throw new ArgumentOutOfRangeException("count");
			if (count >= stack.Count)
				stack.Clear();
			else if (count > 0)
				stack.RemoveRange(stack.Count - count, count);
		}

		public void CopyTop() {
			Push(Peek());
		}

		public override string ToString() {
			if (stack.Count == 0)
				return "<empty>";

			var sb = new StringBuilder();
			const int maxValues = 5;
			for (int i = 0; i < maxValues; i++) {
				int index = stack.Count - i - 1;
				if (index < 0)
					break;
				if (i > 0)
					sb.Append(", ");
				sb.Append(stack[index].ToString());
			}
			if (maxValues < stack.Count)
				sb.Append(", ...");
			return sb.ToString();
		}
	}
}
