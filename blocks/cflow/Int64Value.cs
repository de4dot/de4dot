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
	public class Int64Value : Value {
		public static readonly Int64Value zero = new Int64Value(0);
		public static readonly Int64Value one = new Int64Value(1);

		const ulong NO_UNKNOWN_BITS = ulong.MaxValue;
		public readonly long value;
		public readonly ulong validMask;

		public Int64Value(long value)
			: base(ValueType.Int64) {
			this.value = value;
			this.validMask = NO_UNKNOWN_BITS;
		}

		public Int64Value(long value, ulong validMask)
			: base(ValueType.Int64) {
			this.value = value;
			this.validMask = validMask;
		}

		bool hasUnknownBits() {
			return validMask != NO_UNKNOWN_BITS;
		}

		public bool allBitsValid() {
			return !hasUnknownBits();
		}

		bool isBitValid(int n) {
			return isBitValid(validMask, n);
		}

		static bool isBitValid(ulong validMask, int n) {
			return (validMask & (1UL << n)) != 0;
		}

		public static Int64Value createUnknown() {
			return new Int64Value(0, 0UL);
		}

		public bool isZero() {
			return hasValue(0);
		}

		public bool isNonZero() {
			return ((ulong)value & validMask) != 0;
		}

		public bool hasValue(long value) {
			return allBitsValid() && this.value == value;
		}

		public bool hasValue(ulong value) {
			return hasValue((long)value);
		}

		public static Int64Value Conv_U8(Int32Value a) {
			long value = (long)(ulong)(uint)a.value;
			ulong validMask = a.validMask | (NO_UNKNOWN_BITS << 32);
			return new Int64Value(value, validMask);
		}

		public static Int64Value Conv_U8(Int64Value a) {
			return a;
		}

		public static Int64Value Conv_U8(Real8Value a) {
			return new Int64Value((long)(ulong)a.value);
		}

		public static Int64Value Conv_I8(Int32Value a) {
			long value = a.value;
			ulong validMask = a.validMask;
			if (isBitValid(validMask, 31))
				validMask |= NO_UNKNOWN_BITS << 32;
			else
				validMask &= ~(NO_UNKNOWN_BITS << 32);
			return new Int64Value(value, validMask);
		}

		public static Int64Value Conv_I8(Int64Value a) {
			return a;
		}

		public static Int64Value Conv_I8(Real8Value a) {
			return new Int64Value((long)a.value);
		}

		public static Int64Value Add(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return new Int64Value(a.value + b.value);
			if (ReferenceEquals(a, b))
				return new Int64Value(a.value << 1, (a.validMask << 1) | 1);
			return createUnknown();
		}

		public static Int64Value Sub(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return new Int64Value(a.value - b.value);
			if (ReferenceEquals(a, b))
				return zero;
			return createUnknown();
		}

		public static Int64Value Mul(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return new Int64Value(a.value * b.value);
			if (a.isZero() || b.isZero())
				return zero;
			if (a.hasValue(1))
				return b;
			if (b.hasValue(1))
				return a;
			return createUnknown();
		}

		public static Int64Value Div(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int64Value(a.value / b.value);
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

		public static Int64Value Div_Un(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int64Value((long)((ulong)a.value / (ulong)b.value));
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

		public static Int64Value Rem(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int64Value(a.value % b.value);
				}
				catch (ArithmeticException) {
					return createUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.isNonZero()) || b.hasValue(1))
				return zero;
			return createUnknown();
		}

		public static Int64Value Rem_Un(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid()) {
				try {
					return new Int64Value((long)((ulong)a.value % (ulong)b.value));
				}
				catch (ArithmeticException) {
					return createUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.isNonZero()) || b.hasValue(1))
				return zero;
			return createUnknown();
		}

		public static Int64Value Neg(Int64Value a) {
			if (a.allBitsValid())
				return new Int64Value(-a.value);
			return createUnknown();
		}

		public static Int64Value And(Int64Value a, Int64Value b) {
			long av = a.value, bv = b.value;
			ulong am = a.validMask, bm = b.validMask;
			return new Int64Value(av & bv, (am & bm) | (((ulong)av & am) ^ am) | (((ulong)bv & bm) ^ bm));
		}

		public static Int64Value Or(Int64Value a, Int64Value b) {
			long av = a.value, bv = b.value;
			ulong am = a.validMask, bm = b.validMask;
			return new Int64Value(av | bv, (am & bm) | ((ulong)av & am) | ((ulong)bv & bm));
		}

		public static Int64Value Xor(Int64Value a, Int64Value b) {
			if (ReferenceEquals(a, b))
				return zero;
			long av = a.value, bv = b.value;
			ulong am = a.validMask, bm = b.validMask;
			return new Int64Value(av ^ bv, am & bm);
		}

		public static Int64Value Not(Int64Value a) {
			return new Int64Value(~a.value, a.validMask);
		}

		public static Int64Value Shl(Int64Value a, Int32Value b) {
			if (b.hasUnknownBits())
				return createUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(long) * 8)
				return createUnknown();
			int shift = b.value;
			ulong validMask = (a.validMask << shift) | (ulong.MaxValue >> (sizeof(long) * 8 - shift));
			return new Int64Value(a.value << shift, validMask);
		}

		public static Int64Value Shr(Int64Value a, Int32Value b) {
			if (b.hasUnknownBits())
				return createUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(long) * 8)
				return createUnknown();
			int shift = b.value;
			ulong validMask = a.validMask >> shift;
			if (a.isBitValid(sizeof(long) * 8 - 1))
				validMask |= (ulong.MaxValue << (sizeof(long) * 8 - shift));
			return new Int64Value(a.value >> shift, validMask);
		}

		public static Int64Value Shr_Un(Int64Value a, Int32Value b) {
			if (b.hasUnknownBits())
				return createUnknown();
			if (b.value == 0)
				return a;
			if (b.value < 0 || b.value >= sizeof(long) * 8)
				return createUnknown();
			int shift = b.value;
			ulong validMask = (a.validMask >> shift) | (ulong.MaxValue << (sizeof(long) * 8 - shift));
			return new Int64Value((long)((ulong)a.value >> shift), validMask);
		}

		static Int32Value create(Bool3 b) {
			switch (b) {
			case Bool3.False:	return Int32Value.zero;
			case Bool3.True:	return Int32Value.one;
			default:			return Int32Value.createUnknownBool();
			}
		}

		public static Int32Value Ceq(Int64Value a, Int64Value b) {
			return create(compareEq(a, b));
		}

		public static Int32Value Cgt(Int64Value a, Int64Value b) {
			return create(compareGt(a, b));
		}

		public static Int32Value Cgt_Un(Int64Value a, Int64Value b) {
			return create(compareGt_Un(a, b));
		}

		public static Int32Value Clt(Int64Value a, Int64Value b) {
			return create(compareLt(a, b));
		}

		public static Int32Value Clt_Un(Int64Value a, Int64Value b) {
			return create(compareLt_Un(a, b));
		}

		public static Bool3 compareEq(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value == b.value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.True;
			if (((ulong)a.value & a.validMask & b.validMask) != ((ulong)b.value & a.validMask & b.validMask))
				return Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 compareNeq(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value != b.value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.False;
			if (((ulong)a.value & a.validMask & b.validMask) != ((ulong)b.value & a.validMask & b.validMask))
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 compareGt(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value > b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(long.MinValue))
				return Bool3.False;	// min > x => false
			if (b.hasValue(long.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 compareGt_Un(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (ulong)a.value > (ulong)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(ulong.MinValue))
				return Bool3.False;	// min > x => false
			if (b.hasValue(ulong.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 compareGe(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value >= b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(long.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.hasValue(long.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 compareGe_Un(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (ulong)a.value >= (ulong)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(ulong.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.hasValue(ulong.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 compareLe(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value <= b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(long.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.hasValue(long.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 compareLe_Un(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (ulong)a.value <= (ulong)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(ulong.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.hasValue(ulong.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 compareLt(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return a.value < b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(long.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.hasValue(long.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 compareLt_Un(Int64Value a, Int64Value b) {
			if (a.allBitsValid() && b.allBitsValid())
				return (ulong)a.value < (ulong)b.value ? Bool3.True : Bool3.False;
			if (a.hasValue(ulong.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.hasValue(ulong.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 compareTrue(Int64Value a) {
			if (a.allBitsValid())
				return a.value != 0 ? Bool3.True : Bool3.False;
			if (((ulong)a.value & a.validMask) != 0)
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 compareFalse(Int64Value a) {
			if (a.allBitsValid())
				return a.value == 0 ? Bool3.True : Bool3.False;
			if (((ulong)a.value & a.validMask) != 0)
				return Bool3.False;
			return Bool3.Unknown;
		}

		public override string ToString() {
			if (allBitsValid())
				return value.ToString();
			return string.Format("0x{0:X8}L({1:X8})", value, validMask);
		}
	}
}
