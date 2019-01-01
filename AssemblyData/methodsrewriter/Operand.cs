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

namespace AssemblyData.methodsrewriter {
	class Operand {
		public enum Type {
			ThisArg,		// Replace operand with the 'this' arg
			TempObj,		// Replace operand with temp object local variable
			TempObjArray,	// Replace operand with temp object[] local variable
			OurMethod,		// Replace operand with a call to our method. methodName must be unique.
			NewMethod,		// Replace operand with a call to new method. data is realMethod
			ReflectionType,	// Replace operand with a .NET type
		}

		public Type type;
		public object data;

		public Operand(Type type) {
			this.type = type;
			data = null;
		}

		public Operand(Type type, object data) {
			this.type = type;
			this.data = data;
		}

		public override string ToString() => "{" + type + " => " + data + "}";
	}
}
