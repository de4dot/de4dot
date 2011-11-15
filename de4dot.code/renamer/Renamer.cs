/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.Collections.Generic;
using de4dot.renamer.asmmodules;

namespace de4dot.renamer {
	class Renamer {
		bool renameSymbols = true;
		bool renameFields = true;
		bool renameProperties = true;
		bool renameEvents = true;
		bool renameMethods = true;
		Modules modules = new Modules();

		public Renamer(IEnumerable<IObfuscatedFile> files) {
			foreach (var file in files)
				modules.add(new Module(file));
		}

		public void rename() {
			if (modules.Empty)
				return;
			Log.n("Renaming all obfuscated symbols");

			modules.initialize();
		}
	}
}
