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

namespace de4dot.blocks.cflow {
	public class Int64Value : Value {
		public static readonly Int64Value Zero = new Int64Value(0);
		public static readonly Int64Value One = new Int64Value(1);

		internal const ulong NO_UNKNOWN_BITS = ulong.MaxValue;
		public readonly long Value;
		public readonly ulong ValidMask;

		public Int64Value(long value)
			: base(ValueType.Int64) {
			Value = value;
			ValidMask = NO_UNKNOWN_BITS;
		}

		public Int64Value(long value, ulong validMask)
			: base(ValueType.Int64) {
			Value = value;
			ValidMask = validMask;
		}

		bool HasUnknownBits() => ValidMask != NO_UNKNOWN_BITS;
		public bool AllBitsValid() => !HasUnknownBits();
		bool IsBitValid(int n) => IsBitValid(ValidMask, n);
		static bool IsBitValid(ulong validMask, int n) => (validMask & (1UL << n)) != 0;
		bool AreBitsValid(ulong bitsToTest) => (ValidMask & bitsToTest) == bitsToTest;
		public static Int64Value CreateUnknown() => new Int64Value(0, 0UL);
		public bool IsZero() => HasValue(0);
		public bool IsNonZero() => ((ulong)Value & ValidMask) != 0;
		public bool HasValue(long value) => AllBitsValid() && Value == value;
		public bool HasValue(ulong value) => HasValue((long)value);

		public static Int64Value Conv_U8(Int32Value a) {
			long value = (long)(ulong)(uint)a.Value;
			ulong validMask = a.ValidMask | (NO_UNKNOWN_BITS << 32);
			return new Int64Value(value, validMask);
		}

		public static Int64Value Conv_U8(Int64Value a) => a;

		public static Int64Value Conv_U8(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknown();
			return new Int64Value((long)(ulong)a.Value);
		}

		public static Int64Value Conv_I8(Int32Value a) {
			long value = a.Value;
			ulong validMask = a.ValidMask;
			if (IsBitValid(validMask, 31))
				validMask |= NO_UNKNOWN_BITS << 32;
			else
				validMask &= ~(NO_UNKNOWN_BITS << 32);
			return new Int64Value(value, validMask);
		}

		public static Int64Value Conv_I8(Int64Value a) => a;

		public static Int64Value Conv_I8(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknown();
			return new Int64Value((long)a.Value);
		}

		bool CheckSign(ulong mask) => ((ulong)Value & mask) == 0 || ((ulong)Value & mask) == mask;

		public static Int32Value Conv_Ovf_I1(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 7) ||
				!a.CheckSign(NO_UNKNOWN_BITS << 7))
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_I1(a);
		}

		public static Int32Value Conv_Ovf_I1_Un(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 7) ||
				(ulong)a.Value > (ulong)sbyte.MaxValue)
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_I1(a);
		}

		public static Int32Value Conv_Ovf_I2(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 15) ||
				!a.CheckSign(NO_UNKNOWN_BITS << 15))
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_I2(a);
		}

		public static Int32Value Conv_Ovf_I2_Un(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 15) ||
				(ulong)a.Value > (ulong)short.MaxValue)
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_I2(a);
		}

		public static Int32Value Conv_Ovf_I4(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 31) ||
				!a.CheckSign(NO_UNKNOWN_BITS << 31))
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_I4(a);
		}

		public static Int32Value Conv_Ovf_I4_Un(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 31) ||
				(ulong)a.Value > (ulong)int.MaxValue)
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_I4(a);
		}

		public static Int64Value Conv_Ovf_I8(Int64Value a) => a;

		public static Int64Value Conv_Ovf_I8_Un(Int64Value a) {
			if (!IsBitValid(a.ValidMask, 63) || a.Value < 0)
				return CreateUnknown();
			return a;
		}

		public static Int32Value Conv_Ovf_U1(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 7) ||
				a.Value < 0 || a.Value > byte.MaxValue)
				return Int32Value.CreateUnknownUInt8();
			return Int32Value.Conv_U1(a);
		}

		public static Int32Value Conv_Ovf_U1_Un(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 8) ||
				(ulong)a.Value > byte.MaxValue)
				return Int32Value.CreateUnknownUInt8();
			return Int32Value.Conv_U1(a);
		}

		public static Int32Value Conv_Ovf_U2(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 15) ||
				a.Value < 0 || a.Value > ushort.MaxValue)
				return Int32Value.CreateUnknownUInt16();
			return Int32Value.Conv_U2(a);
		}

		public static Int32Value Conv_Ovf_U2_Un(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 16) ||
				(ulong)a.Value > ushort.MaxValue)
				return Int32Value.CreateUnknownUInt16();
			return Int32Value.Conv_U2(a);
		}

		public static Int32Value Conv_Ovf_U4(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 31) ||
				a.Value < 0 || a.Value > uint.MaxValue)
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_U4(a);
		}

		public static Int32Value Conv_Ovf_U4_Un(Int64Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 32) ||
				(ulong)a.Value > uint.MaxValue)
				return Int32Value.CreateUnknown();
			return Int32Value.Conv_U4(a);
		}

		public static Int64Value Conv_Ovf_U8(Int64Value a) {
			if (!IsBitValid(a.ValidMask, 63) || a.Value < 0)
				return CreateUnknown();
			return a;
		}

		public static Int64Value Conv_Ovf_U8_Un(Int64Value a) => a;

		public static Real8Value Conv_R_Un(Int64Value a) {
			if (a.AllBitsValid())
				return new Real8Value((float)(ulong)a.Value);
			return Real8Value.CreateUnknown();
		}

		public static Real8Value Conv_R4(Int64Value a) {
			if (a.AllBitsValid())
				return new Real8Value((float)(long)a.Value);
			return Real8Value.CreateUnknown();
		}

		public static Real8Value Conv_R8(Int64Value a) {
			if (a.AllBitsValid())
				return new Real8Value((double)(long)a.Value);
			return Real8Value.CreateUnknown();
		}

		public static Int64Value Add(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int64Value(a.Value + b.Value);
			if (ReferenceEquals(a, b))
				return new Int64Value(a.Value << 1, (a.ValidMask << 1) | 1);
			return CreateUnknown();
		}

		public static Int64Value Sub(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int64Value(a.Value - b.Value);
			if (ReferenceEquals(a, b))
				return Zero;
			return CreateUnknown();
		}

		public static Int64Value Mul(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int64Value(a.Value * b.Value);
			if (a.IsZero() || b.IsZero())
				return Zero;
			if (a.HasValue(1))
				return b;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int64Value Div(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value(a.Value / b.Value);
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if (ReferenceEquals(a, b) && a.IsNonZero())
				return One;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int64Value Div_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value((long)((ulong)a.Value / (ulong)b.Value));
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if (ReferenceEquals(a, b) && a.IsNonZero())
				return One;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int64Value Rem(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value(a.Value % b.Value);
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.IsNonZero()) || b.HasValue(1))
				return Zero;
			return CreateUnknown();
		}

		public static Int64Value Rem_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value((long)((ulong)a.Value % (ulong)b.Value));
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.IsNonZero()) || b.HasValue(1))
				return Zero;
			return CreateUnknown();
		}

		public static Int64Value Neg(Int64Value a) {
			if (a.AllBitsValid())
				return new Int64Value(-a.Value);
			return CreateUnknown();
		}

		public static Int64Value Add_Ovf(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value(checked(a.Value + b.Value));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int64Value Add_Ovf_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				ulong aa = (ulong)a.Value, bb = (ulong)b.Value;
				try {
					return new Int64Value((long)checked(aa + bb));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int64Value Sub_Ovf(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value(checked(a.Value - b.Value));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int64Value Sub_Ovf_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				ulong aa = (ulong)a.Value, bb = (ulong)b.Value;
				try {
					return new Int64Value((long)checked(aa - bb));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int64Value Mul_Ovf(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int64Value(checked(a.Value * b.Value));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int64Value Mul_Ovf_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				ulong aa = (ulong)a.Value, bb = (ulong)b.Value;
				try {
					return new Int64Value((long)checked(aa * bb));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int64Value And(Int64Value a, Int64Value b) {
			long av = a.Value, bv = b.Value;
			ulong am = a.ValidMask, bm = b.ValidMask;
			return new Int64Value(av & bv, (am & bm) | (((ulong)av & am) ^ am) | (((ulong)bv & bm) ^ bm));
		}

		public static Int64Value Or(Int64Value a, Int64Value b) {
			long av = a.Value, bv = b.Value;
			ulong am = a.ValidMask, bm = b.ValidMask;
			return new Int64Value(av | bv, (am & bm) | ((ulong)av & am) | ((ulong)bv & bm));
		}

		public static Int64Value Xor(Int64Value a, Int64Value b) {
			if (ReferenceEquals(a, b))
				return Zero;
			long av = a.Value, bv = b.Value;
			ulong am = a.ValidMask, bm = b.ValidMask;
			return new Int64Value(av ^ bv, am & bm);
		}

		public static Int64Value Not(Int64Value a) => new Int64Value(~a.Value, a.ValidMask);

		public static Int64Value Shl(Int64Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.Value == 0)
				return a;
			if (b.Value < 0 || b.Value >= sizeof(long) * 8)
				return CreateUnknown();
			int shift = b.Value;
			ulong validMask = (a.ValidMask << shift) | (ulong.MaxValue >> (sizeof(long) * 8 - shift));
			return new Int64Value(a.Value << shift, validMask);
		}

		public static Int64Value Shr(Int64Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.Value == 0)
				return a;
			if (b.Value < 0 || b.Value >= sizeof(long) * 8)
				return CreateUnknown();
			int shift = b.Value;
			ulong validMask = a.ValidMask >> shift;
			if (a.IsBitValid(sizeof(long) * 8 - 1))
				validMask |= (ulong.MaxValue << (sizeof(long) * 8 - shift));
			return new Int64Value(a.Value >> shift, validMask);
		}

		public static Int64Value Shr_Un(Int64Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.Value == 0)
				return a;
			if (b.Value < 0 || b.Value >= sizeof(long) * 8)
				return CreateUnknown();
			int shift = b.Value;
			ulong validMask = (a.ValidMask >> shift) | (ulong.MaxValue << (sizeof(long) * 8 - shift));
			return new Int64Value((long)((ulong)a.Value >> shift), validMask);
		}

		public static Int32Value Ceq(Int64Value a, Int64Value b) =>
			Int32Value.Create(CompareEq(a, b));

		public static Int32Value Cgt(Int64Value a, Int64Value b) =>
			Int32Value.Create(CompareGt(a, b));

		public static Int32Value Cgt_Un(Int64Value a, Int64Value b) =>
			Int32Value.Create(CompareGt_Un(a, b));

		public static Int32Value Clt(Int64Value a, Int64Value b) =>
			Int32Value.Create(CompareLt(a, b));

		public static Int32Value Clt_Un(Int64Value a, Int64Value b) =>
			Int32Value.Create(CompareLt_Un(a, b));

		public static Bool3 CompareEq(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value == b.Value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.True;
			if (((ulong)a.Value & a.ValidMask & b.ValidMask) != ((ulong)b.Value & a.ValidMask & b.ValidMask))
				return Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareNeq(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value != b.Value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.False;
			if (((ulong)a.Value & a.ValidMask & b.ValidMask) != ((ulong)b.Value & a.ValidMask & b.ValidMask))
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value > b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(long.MinValue))
				return Bool3.False;	// min > x => false
			if (b.HasValue(long.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (ulong)a.Value > (ulong)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(ulong.MinValue))
				return Bool3.False;	// min > x => false
			if (b.HasValue(ulong.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value >= b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(long.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.HasValue(long.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (ulong)a.Value >= (ulong)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(ulong.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.HasValue(ulong.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value <= b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(long.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.HasValue(long.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (ulong)a.Value <= (ulong)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(ulong.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.HasValue(ulong.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value < b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(long.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.HasValue(long.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt_Un(Int64Value a, Int64Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (ulong)a.Value < (ulong)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(ulong.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.HasValue(ulong.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareTrue(Int64Value a) {
			if (a.AllBitsValid())
				return a.Value != 0 ? Bool3.True : Bool3.False;
			if (((ulong)a.Value & a.ValidMask) != 0)
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 CompareFalse(Int64Value a) {
			if (a.AllBitsValid())
				return a.Value == 0 ? Bool3.True : Bool3.False;
			if (((ulong)a.Value & a.ValidMask) != 0)
				return Bool3.False;
			return Bool3.Unknown;
		}

		public override string ToString() {
			if (AllBitsValid())
				return Value.ToString();
			return $"0x{Value:X8}L({ValidMask:X8})";
		}
	}
}
