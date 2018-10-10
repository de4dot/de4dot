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
using System.Collections.Generic;
using de4dot.code.deobfuscators;
using dnlib.DotNet;
using de4dot.code.renamer;

namespace de4dot.code {
	public interface IObfuscatedFile : IDisposable {
		ModuleDefMD ModuleDefMD { get; }
		IDeobfuscator Deobfuscator { get; }
		IDeobfuscatorContext DeobfuscatorContext { get; set; }
		string Filename { get; }
		string NewFilename { get; }
		INameChecker NameChecker { get; }
		bool RenameResourcesInCode { get; }
		bool RemoveNamespaceWithOneType { get; }
		bool RenameResourceKeys { get; }

		void DeobfuscateBegin();
		void Deobfuscate();
		void DeobfuscateEnd();
		void DeobfuscateCleanUp();

		void Load(IList<IDeobfuscator> deobfuscators);
		void Save();
	}
}
