/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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

//TODO: DumpedMethods and DumpedMethod should be moved to dot10

using System;
using System.Collections.Generic;
using dot10.DotNet.MD;

namespace dot10.DotNet {
	public interface IStringDecrypter {
		string decrypt(uint token);
	}

	[Serializable]
	public class DumpedMethods {
		Dictionary<uint, DumpedMethod> methods = new Dictionary<uint, DumpedMethod>();
		IStringDecrypter stringDecrypter = new NoStringDecrypter();

		[Serializable]
		class NoStringDecrypter : IStringDecrypter {
			public string decrypt(uint token) {
				return null;
			}
		}

		public IStringDecrypter StringDecrypter {
			get { return stringDecrypter; }
			set { stringDecrypter = value; }
		}

		public void add(uint token, DumpedMethod info) {
			methods[token] = info;
		}

		public DumpedMethod get(MethodDef method) {
			return get(method.MDToken.ToUInt32());
		}

		public DumpedMethod get(uint token) {
			DumpedMethod dm;
			methods.TryGetValue(token, out dm);
			return dm;
		}

		public void add(DumpedMethod dm) {
			if (MDToken.ToTable(dm.token) != Table.Method || MDToken.ToRID(dm.token) == 0)
				throw new ArgumentException("Invalid token");
			methods[dm.token] = dm;
		}
	}
}
