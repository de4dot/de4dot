/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

namespace de4dot.blocks.cflow {
	public class Int32Value : Value {
		public static readonly Int32Value zero = new Int32Value(0);
		public static readonly Int32Value one = new Int32Value(1);

		const uint NO_UNKNOWN_BITS = uint.MaxValue;
		public readonly int value;
		public readonly uint validMask;

		public Int32Value(int value)
			: base(ValueType.Int32) {
			this.value = value;
			this.validMask = NO_UNKNOWN_BITS;
		}

		public Int32Value(int value, uint validMask)
			: base(ValueType.Int32) {
			this.value = value;
			this.validMask = validMask;
		}

		public bool HasUnknownBits() {
			return validMask != NO_UNKNOWN_BITS;
		}

		public bool AllBitsValid() {
			return !HasUnknownBits();
		}

		bool IsBitValid(int n) {
			return IsBitValid(validMask, n);
		}

		static bool IsBitValid(uint validMask, int n) {
			return (validMask & (1U << n)) != 0;
		}

		public static Int32Value CreateUnknownBool() {
			return new Int32Value(0, NO_UNKNOWN_BITS << 1);
		}

		public static Int32Value CreateUnknownUInt8() {
			return new Int32Value(0, NO_UNKNOWN_BITS << 8);
		}

		public static Int32Value CreateUnknownUInt16() {
			return new Int32Value(0, NO_UNKNOWN_BITS << 16);
		}

		public static Int32Value CreateUnknown() {
			return new Int32Value(0, 0U);
		}

		public bool IsZero() {
			return HasValue(0);
		}

		public bool IsNonZero() {
			return (value & validMask) != 0;
		}

		public bool HasValue(int value) {
			return AllBitsValid() && this.value == value;
		}

		public bool HasValue(uint value) {
			return HasValue((int)value);
		}

		public Int32Value ToBoolean() {
			if (IsNonZero())
				return new Int32Value(1, NO_UNKNOWN_BITS);
			if (IsZero())
				return this;
			return CreateUnknownBool();
		}

		public Int32Value ToInt8() {
			return Conv_I1(this);
		}

		public Int32Value ToUInt8() {
			return Conv_U1(this);
		}

		public Int32Value ToInt16() {
			return Conv_I2(this);
		}

		public Int32Value ToUInt16() {
			return Conv_U2(this);
		}

		public static Int32Value Conv_U1(Int32Value a) {
			return Conv_U1(a.value, a.validMask);
		}

		public static Int32Value Conv_U1(Int64Value a) {
			return Conv_U1((int)a.value, (uint)a.validMask);
		}

		public static Int32Value Conv_U1(int value, uint validMask) {
			value = (int)(byte)value;
			validMask |= NO_UNKNOWN_BITS << 8;
			return new Int32Value(value, validMask);
		}

		public static Int32Value Conv_U1(Real8Value a) {
			return new Int32Value((int)(byte)a.value);
		}

		public static Int32Value Conv_I1(Int32Value a) {
			return Conv_I1(a.value, a.validMask);
		}

		public static Int32Value Conv_I1(Int64Value a) {
			return Conv_I1((int)a.value, (uint)a.validMask);
		}

		public static Int32Value Conv_I1(int value, uint validMask) {
			value = (int)(sbyte)value;
			if (IsBitValid(validMask, 7))
				validMask |= NO_UNKNOWN_BITS << 8;
			else
				validMask &= ~(NO_UNKNOWN_BITS << 8);
			return new Int32Value(value, validMask);
		}

		public static Int32Value Conv_I1(Real8Value a) {
			return new Int32Value((int)(sbyte)a.value);
		}

		public static Int32Value Conv_U2(Int32Value a) {
			return Conv_U2(a.value, a.validMask);
		}

		public static Int32Value Conv_U2(Int64Value a) {
			return Conv_U2((int)a.value, (uint)a.validMask);
		}

		public static Int32Value Conv_U2(int value, uint validMask) {
			value = (int)(ushort)value;
			validMask |= NO_UNKNOWN_BITS << 16;
			return new Int32Value(value, validMask);
		}

		public static Int32Value Conv_U2(Real8Value a) {
			return new Int32Value((int)(ushort)a.value);
		}

		public static Int32Value Conv_I2(Int32Value a) {
			return Conv_I2(a.value, a.validMask);
		}

		public static Int32Value Conv_I2(Int64Value a) {
			return Conv_I2((int)a.value, (uint)a.validMask);
		}

		public static Int32Value Conv_I2(int value, uint validMask) {
			value = (int)(short)value;
			if (IsBitValid(validMask, 15))
				validMask |= NO_UNKNOWN_BITS << 16;
			else
				validMask &= ~(NO_UNKNOWN_BITS << 16);
			return new Int32Value(value, validMask);
		}

		public static Int32Value Conv_I2(Real8Value a) {
			return new Int32Value((int)(short)a.value);
		}

		public static Int32Value Conv_U4(Int32Value a) {
			return a;
		}

		public static Int32Value Conv_U4(Int64Value a) {
			return new Int32Value((int)(uint)a.value, (uint)a.validMask);
		}

		public static Int32Value Conv_U4(Real8Value a) {
			return new Int32Value((int)(uint)a.value);
		}

		public static Int32Value Conv_I4(Int32Value a) {
			return a;
		}

		public static Int32Value Conv_I4(Int64Value a) {
			return new Int32Value((int)a.value, (uint)a.validMask);
		}

		public static Int32Value Conv_I4(Real8Value a) {
			return new Int32Value((int)a.value);
		}

		public static Int32Value Add(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int32Value(a.value + b.value);
			if (ReferenceEquals(a, b))
				return new Int32Value(a.value << 1, (a.validMask << 1) | 1);
			return CreateUnknown();
		}

		public static Int32Value Sub(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int32Value(a.value - b.value);
			if (ReferenceEquals(a, b))
				return zero;
			return CreateUnknown();
		}

		public static Int32Value Mul(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int32Value(a.value * b.value);
			if (a.IsZero() || b.IsZero())
				return zero;
			if (a.HasValue(1))
				return b;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int32Value Div(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(a.value / b.value);
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if (ReferenceEquals(a, b) && a.IsNonZero())
				return one;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int32Value Div_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value((int)((uint)a.value / (uint)b.value));
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if (ReferenceEquals(a, b) && a.IsNonZero())
				return one;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int32Value Rem(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(a.value % b.value);
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.IsNonZero()) || b.HasValue(1))
				return zero;
			return CreateUnknown();
		}

		public static Int32Value Rem_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value((int)((uint)a.value % (uint)b.value));
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.IsNonZero()) || b.HasValue(1))
				return zero;
			return CreateUnknown();
		}

		public static Int32Value Neg(Int32Value a) {
			if (a.AllBitsValid())
				return new Int32Value(-a.value);
			return CreateUnknown();
		}

		public static Int32Value And(Int32Value a, Int32Value b) {
			int av = a.value, bv = b.value;
			uint am = a.validMask, bm = b.validMask;
			return new Int32Value(av & bv, (uint)((am & bm) | ((av & am) ^ am) | ((bv & bm) ^ bm)));
		}

		public static Int32Value Or(Int32Value a, Int32Value b) {
			int av = a.value, bv = b.value;
			uint am = a.validMask, bm = b.validMask;
			return new Int32Value(av | bv, (uint)((am & bm) | (av & am) | (bv & bm)));
		}

		public static Int32Value Xor(Int32Value a, Int32Value b) {
			if (ReferenceEquals(a, b))
				return zero;
			int av = a.value, bv = b.value;
			uint am = a.validMask, bm = b.validMask;
			return new Int32Value(av ^ bv, (uint)(am & bm));
		}

		public static Int32Value Not(Int32Value a) {
			return new Int32Value(~a.value, a.validMask);
		}

		public static Int32Value Shl(Int32Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(int) * 8)
				return CreateUnknown();
			int shift = b.value;
			uint validMask = (a.validMask << shift) | (uint.MaxValue >> (sizeof(int) * 8 - shift));
			return new Int32Value(a.value << shift, validMask);
		}

		public static Int32Value Shr(Int32Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(int) * 8)
				return CreateUnknown();
			int shift = b.value;
			uint validMask = a.validMask >> shift;
			if (a.IsBitValid(sizeof(int) * 8 - 1))
				validMask |= (uint.MaxValue << (sizeof(int) * 8 - shift));
			return new Int32Value(a.value >> shift, validMask);
		}

		public static Int32Value Shr_Un(Int32Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(int) * 8)
				return CreateUnknown();
			int shift = b.value;
			uint validMask = (a.validMask >> shift) | (uint.MaxValue << (sizeof(int) * 8 - shift));
			return new Int32Value((int)((uint)a.value >> shift), validMask);
		}

		static Int32Value create(Bool3 b) {
			switch (b) {
			case Bool3.False:	return zero;
			case Bool3.True:	return one;
			default:			return CreateUnknownBool();
			}
		}

		public static Int32Value Ceq(Int32Value a, Int32Value b) {
			return create(CompareEq(a, b));
		}

		public static Int32Value Cgt(Int32Value a, Int32Value b) {
			return create(CompareGt(a, b));
		}

		public static Int32Value Cgt_Un(Int32Value a, Int32Value b) {
			return create(CompareGt_Un(a, b));
		}

		public static Int32Value Clt(Int32Value a, Int32Value b) {
			return create(CompareLt(a, b));
		}

		public static Int32Value Clt_Un(Int32Value a, Int32Value b) {
			return create(CompareLt_Un(a, b));
		}

		public static Bool3 CompareEq(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.value == b.value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.True;
			if ((a.value & a.validMask & b.validMask) != (b.value & a.validMask & b.validMask))
				return Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareNeq(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.value != b.value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.False;
			if ((a.value & a.validMask & b.validMask) != (b.value & a.validMask & b.validMask))
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.value > b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MinValue))
				return Bool3.False;	// min > x => false
			if (b.HasValue(int.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.value > (uint)b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MinValue))
				return Bool3.False;	// min > x => false
			if (b.HasValue(uint.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.value >= b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.HasValue(int.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.value >= (uint)b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.HasValue(uint.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.value <= b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.HasValue(int.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.value <= (uint)b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.HasValue(uint.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.value < b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.HasValue(int.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.value < (uint)b.value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.HasValue(uint.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareTrue(Int32Value a) {
			if (a.AllBitsValid())
				return a.value != 0 ? Bool3.True : Bool3.False;
			if ((a.value & a.validMask) != 0)
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 CompareFalse(Int32Value a) {
			if (a.AllBitsValid())
				return a.value == 0 ? Bool3.True : Bool3.False;
			if ((a.value & a.validMask) != 0)
				return Bool3.False;
			return Bool3.Unknown;
		}

		public override string ToString() {
			if (AllBitsValid())
				return value.ToString();
			return string.Format("0x{0:X8}({1:X8})", value, validMask);
		}
	}
}
