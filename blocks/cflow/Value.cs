/*
    Copyright (C) 2011 de4dot@gmail.com

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

namespace de4dot.blocks.cflow {
	enum ValueType : byte {
		Unknown,
		Null,
		Boxed,
		Int32,
		Int64,
		Real8,
		String,
	}

	abstract class Value {
		public readonly ValueType valueType;

		protected Value(ValueType valueType) {
			this.valueType = valueType;
		}
	}

	class UnknownValue : Value {
		public UnknownValue()
			: base(ValueType.Unknown) {
		}

		public override string ToString() {
			return "<unknown>";
		}
	}

	class NullValue : Value {
		// There's only one type of null
		public static readonly NullValue Instance = new NullValue();

		NullValue()
			: base(ValueType.Null) {
		}

		public override string ToString() {
			return "null";
		}
	}

	class BoxedValue : Value {
		public readonly Value value;

		public BoxedValue(Value value)
			: base(ValueType.Boxed) {
			this.value = value;
		}

		public override string ToString() {
			return string.Format("box({0})", value.ToString());
		}
	}

	class Int32Value : Value {
		public readonly int value;

		public Int32Value(int value)
			: base(ValueType.Int32) {
			this.value = value;
		}

		public override string ToString() {
			return value.ToString();
		}
	}

	class Int64Value : Value {
		public readonly long value;

		public Int64Value(long value)
			: base(ValueType.Int64) {
			this.value = value;
		}

		public override string ToString() {
			return value.ToString() + "L";
		}
	}

	class Real8Value : Value {
		public readonly double value;

		public Real8Value(double value)
			: base(ValueType.Real8) {
			this.value = value;
		}

		public override string ToString() {
			return value.ToString() + "D";
		}
	}

	class StringValue : Value {
		public readonly string value;

		public StringValue(string value)
			: base(ValueType.String) {
			this.value = value;
		}

		public override string ToString() {
			return string.Format("\"{0}\"", value);
		}
	}
}
