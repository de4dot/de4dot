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

		public bool hasUnknownBits() {
			return validMask != NO_UNKNOWN_BITS;
		}

		public bool allBitsValid() {
			return !hasUnknownBits();
		}

		bool isBitValid(int n) {
			return isBitValid(validMask, n);
		}

		static bool isBitValid(uint validMask, int n) {
			return (validMask & (1U << n)) != 0;
		}

		public static Int32Value createUnknownBool() {
			return new Int32Value(0, NO_UNKNOWN_BITS << 1);
		}

		public static Int32Value createUnknownUInt8() {
			return new Int32Value(0, NO_UNKNOWN_BITS << 8);
		}

		public static Int32Value createUnknownUInt16() {
			return new Int32Value(0, NO_UNKNOWN_BITS << 16);
		}

		public static Int32Value createUnknown() {
			return new Int32Value(0, 0U);
		}

		public bool isZero() {
			return hasValue(0);
		}

		public bool isNonZero() {
			return (value & validMask) != 0;
		}

		public bool hasValue(int value) {
			return allBitsValid() && this.value == value;
		}

		public bool hasValue(uint value) {
			return hasValue((int)value);
		}

		public Int32Value toBoolean() {
			if (isNonZero())
				return new Int32Value(1, NO_UNKNOWN_BITS);
			if (isZero())
				return this;
			return createUnknownBool();
		}

		public Int32Value toInt8() {
			return Conv_I1(this);
		}

		public Int32Value toUInt8() {
			return Conv_U1(this);
		}

		public Int32Value toInt16() {
			return Conv_I2(this);
		}

		public Int32Value toUInt16() {
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
			if (isBitValid(validMask, 7))
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
			if (isBitValid(validMask, 15))
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
			if (a.allBitsValid() && b.allBitsValid())
				return new Int32Value(a.value + b.value);
			if (ReferenceEquals(a, b))
				return new Int32Value(a.value << 1, (a.validMask << 1) | 1);
			return createUnknown();
		}

		public static Int32Value Sub(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return new Int32Value(a.value - b.value);
			if (ReferenceEquals(a, b))
				return zero;
			return createUnknown();
		}

		public static Int32Value Mul(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return new Int32Value(a.value * b.value);
			if (a.isZero() || b.isZero())
				return zero;
			if (a.hasValue(1))
				return b;
			if (b.hasValue(1))
				return a;
			return createUnknown();
		}

		public static Int32Value Div(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int32Value(a.value / b.value);
				}
				catch (ArithmeticException) {
					return createUnknown();
				}
			}
			if (ReferenceEquals(a, b) && a.isNonZero())
				return one;
			if (b.hasValue(1))
				return a;
			return createUnknown();
		}

		public static Int32Value Div_Un(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int32Value((int)((uint)a.value / (uint)b.value));
				}
				catch (ArithmeticException) {
					return createUnknown();
				}
			}
			if (ReferenceEquals(a, b) && a.isNonZero())
				return one;
			if (b.hasValue(1))
				return a;
			return createUnknown();
		}

		public static Int32Value Rem(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int32Value(a.value % b.value);
				}
				catch (ArithmeticException) {
					return createUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.isNonZero()) || b.hasValue(1))
				return zero;
			return createUnknown();
		}

		public static Int32Value Rem_Un(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int32Value((int)((uint)a.value % (uint)b.value));
				}
				catch (ArithmeticException) {
					return createUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.isNonZero()) || b.hasValue(1))
				return zero;
			return createUnknown();
		}

		public static Int32Value Neg(Int32Value a) {
			if (a.allBitsValid())
				return new Int32Value(-a.value);
			return createUnknown();
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
			if (b.hasUnknownBits())
				return createUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(int) * 8)
				return createUnknown();
			int shift = b.value;
			uint validMask = (a.validMask << shift) | (uint.MaxValue >> (sizeof(int) * 8 - shift));
			return new Int32Value(a.value << shift, validMask);
		}

		public static Int32Value Shr(Int32Value a, Int32Value b) {
			if (b.hasUnknownBits())
				return createUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(int) * 8)
				return createUnknown();
			int shift = b.value;
			uint validMask = a.validMask >> shift;
			if (a.isBitValid(sizeof(int) * 8 - 1))
				validMask |= (uint.MaxValue << (sizeof(int) * 8 - shift));
			return new Int32Value(a.value >> shift, validMask);
		}

		public static Int32Value Shr_Un(Int32Value a, Int32Value b) {
			if (b.hasUnknownBits())
				return createUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(int) * 8)
				return createUnknown();
			int shift = b.value;
			uint validMask = (a.validMask >> shift) | (uint.MaxValue << (sizeof(int) * 8 - shift));
			return new Int32Value((int)((uint)a.value >> shift), validMask);
		}

		static Int32Value create(Bool3 b) {
			switch (b) {
			case Bool3.False:	return zero;
			case Bool3.True:	return one;
			default:			return createUnknownBool();
			}
		}

		public static Int32Value Ceq(Int32Value a, Int32Value b) {
			return create(compareEq(a, b));
		}

		public static Int32Value Cgt(Int32Value a, Int32Value b) {
			return create(compareGt(a, b));
		}

		public static Int32Value Cgt_Un(Int32Value a, Int32Value b) {
			return create(compareGt_Un(a, b));
		}

		public static Int32Value Clt(Int32Value a, Int32Value b) {
			return create(compareLt(a, b));
		}

		public static Int32Value Clt_Un(Int32Value a, Int32Value b) {
			return create(compareLt_Un(a, b));
		}

		public static Bool3 compareEq(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value == b.value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.True;
			if ((a.value & a.validMask & b.validMask) != (b.value & a.validMask & b.validMask))
				return Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 compareNeq(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value != b.value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.False;
			if ((a.value & a.validMask & b.validMask) != (b.value & a.validMask & b.validMask))
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 compareGt(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value > b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(int.MinValue))
				return Bool3.False;	// min > x => false
			if (b.hasValue(int.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 compareGt_Un(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (uint)a.value > (uint)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(uint.MinValue))
				return Bool3.False;	// min > x => false
			if (b.hasValue(uint.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 compareGe(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value >= b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(int.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.hasValue(int.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 compareGe_Un(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (uint)a.value >= (uint)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(uint.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.hasValue(uint.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 compareLe(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value <= b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(int.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.hasValue(int.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 compareLe_Un(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (uint)a.value <= (uint)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(uint.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.hasValue(uint.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 compareLt(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value < b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(int.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.hasValue(int.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 compareLt_Un(Int32Value a, Int32Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (uint)a.value < (uint)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(uint.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.hasValue(uint.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 compareTrue(Int32Value a) {
			if (a.allBitsValid())
				return a.value != 0 ? Bool3.True : Bool3.False;
			if ((a.value & a.validMask) != 0)
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 compareFalse(Int32Value a) {
			if (a.allBitsValid())
				return a.value == 0 ? Bool3.True : Bool3.False;
			if ((a.value & a.validMask) != 0)
				return Bool3.False;
			return Bool3.Unknown;
		}

		public override string ToString() {
			if (allBitsValid())
				return value.ToString();
			return string.Format("0x{0:X8}({1:X8})", value, validMask);
		}
	}
}
