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

using dnlib.DotNet;
using dnlib.DotNet.MD;
using dnlib.DotNet.Writer;

namespace de4dot.code {
	public class PrintNewTokens : IModuleWriterListener {
		readonly ModuleDef module;
		readonly IModuleWriterListener otherListener;

		public PrintNewTokens(ModuleDef module, IModuleWriterListener otherListener) {
			this.module = module;
			this.otherListener = otherListener;
		}

		public void OnWriterEvent(ModuleWriterBase writer, ModuleWriterEvent evt) {
			if (otherListener != null)
				otherListener.OnWriterEvent(writer, evt);
			if (evt == ModuleWriterEvent.End)
				PrintTokens(writer);
		}

		void PrintTokens(ModuleWriterBase writer) {
			if (Logger.Instance.IgnoresEvent(LoggerEvent.Verbose))
				return;

			var md = writer.MetaData;

			Logger.v("Old -> new tokens: Assembly: {0} (module: {1})", module.Assembly, module.Location);
			Logger.Instance.Indent();
			foreach (var type in module.GetTypes()) {
				uint newRid;

				newRid = md.GetRid(type);
				if (newRid == 0)
					continue;
				Logger.v("{0:X8} -> {1:X8} Type: {2}",
						type.MDToken.ToUInt32(),
						new MDToken(Table.TypeDef, newRid).ToUInt32(),
						Utils.RemoveNewlines(type));

				Logger.Instance.Indent();

				foreach (var method in type.Methods) {
					newRid = md.GetRid(method);
					if (newRid == 0)
						continue;
					Logger.v("{0:X8} -> {1:X8} Method: {2}",
							method.MDToken.ToUInt32(),
							new MDToken(Table.Method, newRid).ToUInt32(),
							Utils.RemoveNewlines(method));
				}

				foreach (var field in type.Fields) {
					newRid = md.GetRid(field);
					if (newRid == 0)
						continue;
					Logger.v("{0:X8} -> {1:X8} Field: {2}",
							field.MDToken.ToUInt32(),
							new MDToken(Table.Field, newRid).ToUInt32(),
							Utils.RemoveNewlines(field));
				}

				foreach (var prop in type.Properties) {
					newRid = md.GetRid(prop);
					if (newRid == 0)
						continue;
					Logger.v("{0:X8} -> {1:X8} Property: {2}",
							prop.MDToken.ToUInt32(),
							new MDToken(Table.Property, newRid).ToUInt32(),
							Utils.RemoveNewlines(prop));
				}

				foreach (var evt in type.Events) {
					newRid = md.GetRid(evt);
					if (newRid == 0)
						continue;
					Logger.v("{0:X8} -> {1:X8} Event: {2}",
							evt.MDToken.ToUInt32(),
							new MDToken(Table.Event, newRid).ToUInt32(),
							Utils.RemoveNewlines(evt));
				}

				Logger.Instance.DeIndent();
			}
			Logger.Instance.DeIndent();
		}
	}
}
