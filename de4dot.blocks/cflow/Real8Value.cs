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
	public class Real8Value : Value {
		public readonly double Value;
		public readonly bool IsValid;

		public Real8Value(double value)
			: base(ValueType.Real8) {
			this.Value = value;
			this.IsValid = true;
		}

		public Real8Value(double value, bool isValid)
			: base(ValueType.Real8) {
			this.Value = value;
			this.IsValid = isValid;
		}

		public static Real8Value CreateUnknown() {
			return new Real8Value(0, false);
		}

		public Real8Value ToSingle() {
			if (!IsValid)
				return CreateUnknown();
			return new Real8Value((float)Value);
		}

		public static Real8Value Add(Real8Value a, Real8Value b) {
			if (!a.IsValid || !b.IsValid)
				return CreateUnknown();
			return new Real8Value(a.Value + b.Value);
		}

		public static Real8Value Sub(Real8Value a, Real8Value b) {
			if (!a.IsValid || !b.IsValid)
				return CreateUnknown();
			return new Real8Value(a.Value - b.Value);
		}

		public static Real8Value Mul(Real8Value a, Real8Value b) {
			if (!a.IsValid || !b.IsValid)
				return CreateUnknown();
			return new Real8Value(a.Value * b.Value);
		}

		public static Real8Value Div(Real8Value a, Real8Value b) {
			if (!a.IsValid || !b.IsValid)
				return CreateUnknown();
			return new Real8Value(a.Value / b.Value);
		}

		public static Real8Value Rem(Real8Value a, Real8Value b) {
			if (!a.IsValid || !b.IsValid)
				return CreateUnknown();
			return new Real8Value(a.Value % b.Value);
		}

		public static Real8Value Neg(Real8Value a) {
			if (!a.IsValid)
				return CreateUnknown();
			return new Real8Value(-a.Value);
		}

		public static Real8Value Add_Ovf(Real8Value a, Real8Value b) {
			return CreateUnknown();
		}

		public static Real8Value Add_Ovf_Un(Real8Value a, Real8Value b) {
			return CreateUnknown();
		}

		public static Real8Value Sub_Ovf(Real8Value a, Real8Value b) {
			return CreateUnknown();
		}

		public static Real8Value Sub_Ovf_Un(Real8Value a, Real8Value b) {
			return CreateUnknown();
		}

		public static Real8Value Mul_Ovf(Real8Value a, Real8Value b) {
			return CreateUnknown();
		}

		public static Real8Value Mul_Ovf_Un(Real8Value a, Real8Value b) {
			return CreateUnknown();
		}

		public static Int32Value Conv_Ovf_I1(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_I1_Un(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_I2(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_I2_Un(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_I4(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_I4_Un(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int64Value Conv_Ovf_I8(Real8Value a) {
			return Int64Value.CreateUnknown();
		}

		public static Int64Value Conv_Ovf_I8_Un(Real8Value a) {
			return Int64Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_U1(Real8Value a) {
			return Int32Value.CreateUnknownUInt8();
		}

		public static Int32Value Conv_Ovf_U1_Un(Real8Value a) {
			return Int32Value.CreateUnknownUInt8();
		}

		public static Int32Value Conv_Ovf_U2(Real8Value a) {
			return Int32Value.CreateUnknownUInt16();
		}

		public static Int32Value Conv_Ovf_U2_Un(Real8Value a) {
			return Int32Value.CreateUnknownUInt16();
		}

		public static Int32Value Conv_Ovf_U4(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int32Value Conv_Ovf_U4_Un(Real8Value a) {
			return Int32Value.CreateUnknown();
		}

		public static Int64Value Conv_Ovf_U8(Real8Value a) {
			return Int64Value.CreateUnknown();
		}

		public static Int64Value Conv_Ovf_U8_Un(Real8Value a) {
			return Int64Value.CreateUnknown();
		}

		public static Real8Value Conv_R_Un(Real8Value a) {
			return CreateUnknown();
		}

		public static Real8Value Conv_R4(Real8Value a) {
			if (a.IsValid)
				return new Real8Value((float)a.Value);
			return CreateUnknown();
		}

		public static Real8Value Conv_R8(Real8Value a) {
			return a;
		}

		public static Int32Value Ceq(Real8Value a, Real8Value b) {
			return Int32Value.Create(CompareEq(a, b));
		}

		public static Int32Value Cgt(Real8Value a, Real8Value b) {
			return Int32Value.Create(CompareGt(a, b));
		}

		public static Int32Value Cgt_Un(Real8Value a, Real8Value b) {
			return Int32Value.Create(CompareGt_Un(a, b));
		}

		public static Int32Value Clt(Real8Value a, Real8Value b) {
			return Int32Value.Create(CompareLt(a, b));
		}

		public static Int32Value Clt_Un(Real8Value a, Real8Value b) {
			return Int32Value.Create(CompareLt_Un(a, b));
		}

		public static Bool3 CompareEq(Real8Value a, Real8Value b) {
			if (a.IsValid && b.IsValid)
				return a.Value == b.Value ? Bool3.True : Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareNeq(Real8Value a, Real8Value b) {
			if (a.IsValid && b.IsValid)
				return a.Value != b.Value ? Bool3.True : Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt(Real8Value a, Real8Value b) {
			if (a.IsValid && b.IsValid)
				return a.Value > b.Value ? Bool3.True : Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareGt_Un(Real8Value a, Real8Value b) {
			return Bool3.Unknown;	//TODO:
		}

		public static Bool3 CompareGe(Real8Value a, Real8Value b) {
			if (a.IsValid && b.IsValid)
				return a.Value >= b.Value ? Bool3.True : Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareGe_Un(Real8Value a, Real8Value b) {
			return Bool3.Unknown;	//TODO:
		}

		public static Bool3 CompareLe(Real8Value a, Real8Value b) {
			if (a.IsValid && b.IsValid)
				return a.Value <= b.Value ? Bool3.True : Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareLe_Un(Real8Value a, Real8Value b) {
			return Bool3.Unknown;	//TODO:
		}

		public static Bool3 CompareLt(Real8Value a, Real8Value b) {
			if (a.IsValid && b.IsValid)
				return a.Value < b.Value ? Bool3.True : Bool3.False;
			return Bool3.Unknown;
		}

		public static Bool3 CompareLt_Un(Real8Value a, Real8Value b) {
			return Bool3.Unknown;	//TODO:
		}

		public static Bool3 CompareTrue(Real8Value a) {
			return Bool3.Unknown;
		}

		public static Bool3 CompareFalse(Real8Value a) {
			return Bool3.Unknown;
		}

		public override string ToString() {
			if (!IsValid)
				return "<INVALID_REAL8>";
			return Value.ToString();
		}
	}
}
