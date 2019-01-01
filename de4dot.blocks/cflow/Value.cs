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

namespace de4dot.blocks.cflow {
	public enum ValueType : byte {
		Unknown,
		Null,
		Object,
		Boxed,
		Int32,
		Int64,
		Real8,
		String,
	}

	public enum Bool3 {
		Unknown = -1,
		False,
		True,
	}

	public abstract class Value {
		public readonly ValueType valueType;
		public bool IsUnknown() => valueType == ValueType.Unknown;
		public bool IsNull() => valueType == ValueType.Null;
		public bool IsObject() => valueType == ValueType.Object;
		public bool IsBoxed() => valueType == ValueType.Boxed;
		public bool IsInt32() => valueType == ValueType.Int32;
		public bool IsInt64() => valueType == ValueType.Int64;
		public bool IsReal8() => valueType == ValueType.Real8;
		public bool IsString() => valueType == ValueType.String;
		protected Value(ValueType valueType) => this.valueType = valueType;
	}

	public class UnknownValue : Value {
		public UnknownValue() : base(ValueType.Unknown) { }
		public override string ToString() => "<unknown>";
	}

	public class ObjectValue : Value {
		public readonly object obj;	// can be null but that doesn't mean that this ObjectValue instance is null
		public ObjectValue() : this(null) { }
		public ObjectValue(object obj) : base(ValueType.Object) => this.obj = obj;
		public override string ToString() => "<non-null object>";
	}

	public class NullValue : Value {
		// There's only one type of null
		public static readonly NullValue Instance = new NullValue();
		NullValue() : base(ValueType.Null) { }
		public override string ToString() => "null";
	}

	public class BoxedValue : Value {
		public readonly Value value;
		public BoxedValue(Value value) : base(ValueType.Boxed) => this.value = value;
		public override string ToString() => $"box({value.ToString()})";
	}

	public class StringValue : Value {
		public readonly string value;
		public StringValue(string value) : base(ValueType.String) => this.value = value;
		public override string ToString() => $"\"{value}\"";
	}
}
