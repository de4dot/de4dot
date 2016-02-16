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
	public class Int32Value : Value {
		public static readonly Int32Value Zero = new Int32Value(0);
		public static readonly Int32Value One = new Int32Value(1);

		internal const uint NO_UNKNOWN_BITS = uint.MaxValue;
		public readonly int Value;
		public readonly uint ValidMask;

		public Int32Value(int value)
			: base(ValueType.Int32) {
			this.Value = value;
			this.ValidMask = NO_UNKNOWN_BITS;
		}

		public Int32Value(int value, uint validMask)
			: base(ValueType.Int32) {
			this.Value = value;
			this.ValidMask = validMask;
		}

		public bool HasUnknownBits() {
			return ValidMask != NO_UNKNOWN_BITS;
		}

		public bool AllBitsValid() {
			return !HasUnknownBits();
		}

		bool IsBitValid(int n) {
			return IsBitValid(ValidMask, n);
		}

		static bool IsBitValid(uint validMask, int n) {
			return (validMask & (1U << n)) != 0;
		}

		bool AreBitsValid(uint bitsToTest) {
			return (ValidMask & bitsToTest) == bitsToTest;
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
			return ((uint)Value & ValidMask) != 0;
		}

		public bool HasValue(int value) {
			return AllBitsValid() && this.Value == value;
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
			return Conv_U1(a.Value, a.ValidMask);
		}

		public static Int32Value Conv_U1(Int64Value a) {
			return Conv_U1((int)a.Value, (uint)a.ValidMask);
		}

		public static Int32Value Conv_U1(int value, uint validMask) {
			value = (int)(byte)value;
			validMask |= NO_UNKNOWN_BITS << 8;
			return new Int32Value(value, validMask);
		}

		public static Int32Value Conv_U1(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknownUInt8();
			return new Int32Value((int)(byte)a.Value);
		}

		public static Int32Value Conv_I1(Int32Value a) {
			return Conv_I1(a.Value, a.ValidMask);
		}

		public static Int32Value Conv_I1(Int64Value a) {
			return Conv_I1((int)a.Value, (uint)a.ValidMask);
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
			if (!a.IsValid)
				return CreateUnknown();
			return new Int32Value((int)(sbyte)a.Value);
		}

		public static Int32Value Conv_U2(Int32Value a) {
			return Conv_U2(a.Value, a.ValidMask);
		}

		public static Int32Value Conv_U2(Int64Value a) {
			return Conv_U2((int)a.Value, (uint)a.ValidMask);
		}

		public static Int32Value Conv_U2(int value, uint validMask) {
			value = (int)(ushort)value;
			validMask |= NO_UNKNOWN_BITS << 16;
			return new Int32Value(value, validMask);
		}

		public static Int32Value Conv_U2(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknownUInt16();
			return new Int32Value((int)(ushort)a.Value);
		}

		public static Int32Value Conv_I2(Int32Value a) {
			return Conv_I2(a.Value, a.ValidMask);
		}

		public static Int32Value Conv_I2(Int64Value a) {
			return Conv_I2((int)a.Value, (uint)a.ValidMask);
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
			if (!a.IsValid)
				return CreateUnknown();
			return new Int32Value((int)(short)a.Value);
		}

		public static Int32Value Conv_U4(Int32Value a) {
			return a;
		}

		public static Int32Value Conv_U4(Int64Value a) {
			return new Int32Value((int)(uint)a.Value, (uint)a.ValidMask);
		}

		public static Int32Value Conv_U4(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknown();
			return new Int32Value((int)(uint)a.Value);
		}

		public static Int32Value Conv_I4(Int32Value a) {
			return a;
		}

		public static Int32Value Conv_I4(Int64Value a) {
			return new Int32Value((int)a.Value, (uint)a.ValidMask);
		}

		public static Int32Value Conv_I4(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknown();
			return new Int32Value((int)a.Value);
		}

		bool CheckSign(uint mask) {
			return ((uint)Value & mask) == 0 || ((uint)Value & mask) == mask;
		}

		public static Int32Value Conv_Ovf_I1(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 7) ||
				!a.CheckSign(NO_UNKNOWN_BITS << 7))
				return CreateUnknown();
			return Conv_I1(a);
		}

		public static Int32Value Conv_Ovf_I1_Un(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 7) ||
				(uint)a.Value > sbyte.MaxValue)
				return CreateUnknown();
			return Conv_I1(a);
		}

		public static Int32Value Conv_Ovf_I2(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 15) ||
				!a.CheckSign(NO_UNKNOWN_BITS << 15))
				return CreateUnknown();
			return Conv_I2(a);
		}

		public static Int32Value Conv_Ovf_I2_Un(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 15) ||
				(uint)a.Value > short.MaxValue)
				return CreateUnknown();
			return Conv_I2(a);
		}

		public static Int32Value Conv_Ovf_I4(Int32Value a) {
			return a;
		}

		public static Int32Value Conv_Ovf_I4_Un(Int32Value a) {
			if (!IsBitValid(a.ValidMask, 31) || a.Value < 0)
				return CreateUnknown();
			return a;
		}

		public static Int64Value Conv_Ovf_I8(Int32Value a) {
			ulong validMask = a.ValidMask;
			if (IsBitValid(a.ValidMask, 31))
				validMask |= Int64Value.NO_UNKNOWN_BITS << 32;
			return new Int64Value(a.Value, validMask);
		}

		public static Int64Value Conv_Ovf_I8_Un(Int32Value a) {
			return new Int64Value((long)(uint)a.Value, a.ValidMask | (Int64Value.NO_UNKNOWN_BITS << 32));
		}

		public static Int32Value Conv_Ovf_U1(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 7) ||
				a.Value < 0 || a.Value > byte.MaxValue)
				return CreateUnknownUInt8();
			return Conv_U1(a);
		}

		public static Int32Value Conv_Ovf_U1_Un(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 8) ||
				(uint)a.Value > byte.MaxValue)
				return CreateUnknownUInt8();
			return Conv_U1(a);
		}

		public static Int32Value Conv_Ovf_U2(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 15) ||
				a.Value < 0 || a.Value > ushort.MaxValue)
				return CreateUnknownUInt16();
			return Conv_U2(a);
		}

		public static Int32Value Conv_Ovf_U2_Un(Int32Value a) {
			if (!a.AreBitsValid(NO_UNKNOWN_BITS << 16) ||
				(uint)a.Value > ushort.MaxValue)
				return CreateUnknownUInt16();
			return Conv_U2(a);
		}

		public static Int32Value Conv_Ovf_U4(Int32Value a) {
			if (!IsBitValid(a.ValidMask, 31) || a.Value < 0)
				return CreateUnknown();
			return a;
		}

		public static Int32Value Conv_Ovf_U4_Un(Int32Value a) {
			return a;
		}

		public static Int64Value Conv_Ovf_U8(Int32Value a) {
			if (!IsBitValid(a.ValidMask, 31) || a.Value < 0)
				return Int64Value.CreateUnknown();
			return new Int64Value(a.Value, (ulong)a.ValidMask | (Int64Value.NO_UNKNOWN_BITS << 32));
		}

		public static Int64Value Conv_Ovf_U8_Un(Int32Value a) {
			return new Int64Value((long)(uint)a.Value, a.ValidMask | (Int64Value.NO_UNKNOWN_BITS << 32));
		}

		public static Real8Value Conv_R_Un(Int32Value a) {
			if (a.AllBitsValid())
				return new Real8Value((double)(uint)a.Value);
			return Real8Value.CreateUnknown();
		}

		public static Real8Value Conv_R4(Int32Value a) {
			if (a.AllBitsValid())
				return new Real8Value((double)(int)a.Value);
			return Real8Value.CreateUnknown();
		}

		public static Real8Value Conv_R8(Int32Value a) {
			if (a.AllBitsValid())
				return new Real8Value((double)(int)a.Value);
			return Real8Value.CreateUnknown();
		}

		public static Int32Value Add(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int32Value(a.Value + b.Value);
			if (ReferenceEquals(a, b))
				return new Int32Value(a.Value << 1, (a.ValidMask << 1) | 1);
			return CreateUnknown();
		}

		public static Int32Value Sub(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int32Value(a.Value - b.Value);
			if (ReferenceEquals(a, b))
				return Zero;
			return CreateUnknown();
		}

		public static Int32Value Mul(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return new Int32Value(a.Value * b.Value);
			if (a.IsZero() || b.IsZero())
				return Zero;
			if (a.HasValue(1))
				return b;
			if (b.HasValue(1))
				return a;
			return CreateUnknown();
		}

		public static Int32Value Div(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(a.Value / b.Value);
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

		public static Int32Value Div_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value((int)((uint)a.Value / (uint)b.Value));
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

		public static Int32Value Rem(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(a.Value % b.Value);
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.IsNonZero()) || b.HasValue(1))
				return Zero;
			return CreateUnknown();
		}

		public static Int32Value Rem_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value((int)((uint)a.Value % (uint)b.Value));
				}
				catch (ArithmeticException) {
					return CreateUnknown();
				}
			}
			if ((ReferenceEquals(a, b) && a.IsNonZero()) || b.HasValue(1))
				return Zero;
			return CreateUnknown();
		}

		public static Int32Value Neg(Int32Value a) {
			if (a.AllBitsValid())
				return new Int32Value(-a.Value);
			return CreateUnknown();
		}

		public static Int32Value Add_Ovf(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(checked(a.Value + b.Value));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int32Value Add_Ovf_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				uint aa = (uint)a.Value, bb = (uint)b.Value;
				try {
					return new Int32Value((int)checked(aa + bb));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int32Value Sub_Ovf(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(checked(a.Value - b.Value));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int32Value Sub_Ovf_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				uint aa = (uint)a.Value, bb = (uint)b.Value;
				try {
					return new Int32Value((int)checked(aa - bb));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int32Value Mul_Ovf(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				try {
					return new Int32Value(checked(a.Value * b.Value));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int32Value Mul_Ovf_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid()) {
				uint aa = (uint)a.Value, bb = (uint)b.Value;
				try {
					return new Int32Value((int)checked(aa * bb));
				}
				catch (OverflowException) {
				}
			}
			return CreateUnknown();
		}

		public static Int32Value And(Int32Value a, Int32Value b) {
			int av = a.Value, bv = b.Value;
			uint am = a.ValidMask, bm = b.ValidMask;
			return new Int32Value(av & bv, (am & bm) | (((uint)av & am) ^ am) | (((uint)bv & bm) ^ bm));
		}

		public static Int32Value Or(Int32Value a, Int32Value b) {
			int av = a.Value, bv = b.Value;
			uint am = a.ValidMask, bm = b.ValidMask;
			return new Int32Value(av | bv, (am & bm) | ((uint)av & am) | ((uint)bv & bm));
		}

		public static Int32Value Xor(Int32Value a, Int32Value b) {
			if (ReferenceEquals(a, b))
				return Zero;
			int av = a.Value, bv = b.Value;
			uint am = a.ValidMask, bm = b.ValidMask;
			return new Int32Value(av ^ bv, am & bm);
		}

		public static Int32Value Not(Int32Value a) {
			return new Int32Value(~a.Value, a.ValidMask);
		}

		public static Int32Value Shl(Int32Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.Value == 0)
				return a;
			if (b.Value < 0 || b.Value >= sizeof(int) * 8)
				return CreateUnknown();
			int shift = b.Value;
			uint validMask = (a.ValidMask << shift) | (uint.MaxValue >> (sizeof(int) * 8 - shift));
			return new Int32Value(a.Value << shift, validMask);
		}

		public static Int32Value Shr(Int32Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.Value == 0)
				return a;
			if (b.Value < 0 || b.Value >= sizeof(int) * 8)
				return CreateUnknown();
			int shift = b.Value;
			uint validMask = a.ValidMask >> shift;
			if (a.IsBitValid(sizeof(int) * 8 - 1))
				validMask |= (uint.MaxValue << (sizeof(int) * 8 - shift));
			return new Int32Value(a.Value >> shift, validMask);
		}

		public static Int32Value Shr_Un(Int32Value a, Int32Value b) {
			if (b.HasUnknownBits())
				return CreateUnknown();
			if (b.Value == 0)
				return a;
			if (b.Value < 0 || b.Value >= sizeof(int) * 8)
				return CreateUnknown();
			int shift = b.Value;
			uint validMask = (a.ValidMask >> shift) | (uint.MaxValue << (sizeof(int) * 8 - shift));
			return new Int32Value((int)((uint)a.Value >> shift), validMask);
		}

		public static Int32Value Create(Bool3 b) {
			switch (b) {
			case Bool3.False:	return Zero;
			case Bool3.True:	return One;
			default:			return CreateUnknownBool();
			}
		}

		public static Int32Value Ceq(Int32Value a, Int32Value b) {
			return Create(CompareEq(a, b));
		}

		public static Int32Value Cgt(Int32Value a, Int32Value b) {
			return Create(CompareGt(a, b));
		}

		public static Int32Value Cgt_Un(Int32Value a, Int32Value b) {
			return Create(CompareGt_Un(a, b));
		}

		public static Int32Value Clt(Int32Value a, Int32Value b) {
			return Create(CompareLt(a, b));
		}

		public static Int32Value Clt_Un(Int32Value a, Int32Value b) {
			return Create(CompareLt_Un(a, b));
		}

		public static Bool3 CompareEq(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value == b.Value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.True;
			if (((uint)a.Value & a.ValidMask & b.ValidMask) != ((uint)b.Value & a.ValidMask & b.ValidMask))
				return Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareNeq(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value != b.Value ? Bool3.True : Bool3.False;
			if (ReferenceEquals(a, b))
				return Bool3.False;
			if (((uint)a.Value & a.ValidMask & b.ValidMask) != ((uint)b.Value & a.ValidMask & b.ValidMask))
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value > b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MinValue))
				return Bool3.False;	// min > x => false
			if (b.HasValue(int.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.Value > (uint)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MinValue))
				return Bool3.False;	// min > x => false
			if (b.HasValue(uint.MaxValue))
				return Bool3.False;	// x > max => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value >= b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.HasValue(int.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.Value >= (uint)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MaxValue))
				return Bool3.True;	// max >= x => true
			if (b.HasValue(uint.MinValue))
				return Bool3.True;	// x >= min => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value <= b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.HasValue(int.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.Value <= (uint)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MinValue))
				return Bool3.True;	// min <= x => true
			if (b.HasValue(uint.MaxValue))
				return Bool3.True;	// x <= max => true
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return a.Value < b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(int.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.HasValue(int.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt_Un(Int32Value a, Int32Value b) {
			if (a.AllBitsValid() && b.AllBitsValid())
				return (uint)a.Value < (uint)b.Value ? Bool3.True : Bool3.False;
			if (a.HasValue(uint.MaxValue))
				return Bool3.False;	// max < x => false
			if (b.HasValue(uint.MinValue))
				return Bool3.False;	// x < min => false
			return Bool3.Unknown;
		}

		public static Bool3 CompareTrue(Int32Value a) {
			if (a.AllBitsValid())
				return a.Value != 0 ? Bool3.True : Bool3.False;
			if (((uint)a.Value & a.ValidMask) != 0)
				return Bool3.True;
			return Bool3.Unknown;
		}

		public static Bool3 CompareFalse(Int32Value a) {
			if (a.AllBitsValid())
				return a.Value == 0 ? Bool3.True : Bool3.False;
			if (((uint)a.Value & a.ValidMask) != 0)
				return Bool3.False;
			return Bool3.Unknown;
		}

		public override string ToString() {
			if (AllBitsValid())
				return Value.ToString();
			return string.Format("0x{0:X8}({1:X8})", Value, ValidMask);
		}
	}
}
