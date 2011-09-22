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

#include <stdio.h>
#include <stdarg.h>
#include "log.h"

enum {
	error,
	warning,
	normal,
	verbose,
	veryverbose,
};

static int logLevel = normal;

void setLogLevel(int level) {
	logLevel = level;
}

static void log(int level, const char* format, va_list valist) {
	if (level <= logLevel)
		vprintf(format, valist);
}

void loge(const char* format, ...) {
	va_list argptr;
	va_start(argptr, format);
	log(error, format, argptr);
	va_end(argptr);
}

void logw(const char* format, ...) {
	va_list argptr;
	va_start(argptr, format);
	log(warning, format, argptr);
	va_end(argptr);
}

void logn(const char* format, ...) {
	va_list argptr;
	va_start(argptr, format);
	log(normal, format, argptr);
	va_end(argptr);
}

void logv(const char* format, ...) {
	va_list argptr;
	va_start(argptr, format);
	log(verbose, format, argptr);
	va_end(argptr);
}

void logvv(const char* format, ...) {
	va_list argptr;
	va_start(argptr, format);
	log(veryverbose, format, argptr);
	va_end(argptr);
}
