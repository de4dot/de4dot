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

namespace de4dot.blocks.cflow {
	public class Real8Value : Value {
		public readonly double value;

		public Real8Value(double value)
			: base(ValueType.Real8) {
			this.value = value;
		}

		public static Real8Value Add(Real8Value a, Real8Value b) {
			return new Real8Value(a.value + b.value);
		}

		public static Real8Value Sub(Real8Value a, Real8Value b) {
			return new Real8Value(a.value - b.value);
		}

		public static Real8Value Mul(Real8Value a, Real8Value b) {
			return new Real8Value(a.value * b.value);
		}

		public static Real8Value Div(Real8Value a, Real8Value b) {
			return new Real8Value(a.value / b.value);
		}

		public static Real8Value Rem(Real8Value a, Real8Value b) {
			return new Real8Value(a.value % b.value);
		}

		public static Real8Value Neg(Real8Value a) {
			return new Real8Value(-a.value);
		}
	}
}
