/*
    This file is part of EqualizerAPO, a system-wide equalizer.
    Copyright (C) 2012  Jonas Thedering
    Modified for OmniControl in 2026: own COM class ids, registry key and file names (see native/EqualizerAPO/README.md).

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along
    with this program; if not, write to the Free Software Foundation, Inc.,
    51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
*/

#pragma once

#include <stdexcept>
#include <string>
#include <vector>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// {27AB1524-86BE-4216-B86B-EC533B2B90B6} (OmniControl APO pre-mix; upstream Equalizer APO uses EACD2258-...)
const GUID EQUALIZERAPO_PRE_MIX_GUID = {0x27ab1524, 0x86be, 0x4216, {0xb8, 0x6b, 0xec, 0x53, 0x3b, 0x2b, 0x90, 0xb6}};
// {7F3F0D64-5AAB-432D-943C-C0B715C12DA0} (OmniControl APO post-mix; upstream Equalizer APO uses EC1CC9CE-...)
const GUID EQUALIZERAPO_POST_MIX_GUID = {0x7f3f0d64, 0x5aab, 0x432d, {0x94, 0x3c, 0xc0, 0xb7, 0x15, 0xc1, 0x2d, 0xa0}};

#define APP_REGPATH L"HKEY_LOCAL_MACHINE\\SOFTWARE\\OmniControlAPO"
#define USER_REGPATH L"HKEY_CURRENT_USER\\SOFTWARE\\OmniControlAPO"

class RegistryHelper
{
public:
	static std::wstring readValue(std::wstring key, std::wstring valuename);
	static unsigned long readDWORDValue(std::wstring key, std::wstring valuename);
	static std::vector<std::wstring> readMultiValue(std::wstring key, std::wstring valuename);
	static std::vector<unsigned char> readBinaryValue(std::wstring key, std::wstring valuename);
	static void writeValue(std::wstring key, std::wstring valuename, std::wstring value);
	static void writeDWORDValue(std::wstring key, std::wstring valuename, unsigned long value);
	static void writeMultiValue(std::wstring key, std::wstring valuename, std::wstring value);
	static void writeMultiValue(std::wstring key, std::wstring valuename, std::vector<std::wstring> values);
	static void deleteValue(std::wstring key, std::wstring valuename);
	static void createKey(std::wstring key);
	static void deleteKey(std::wstring key);
	static void makeWritable(std::wstring key);
	static void takeOwnership(std::wstring key);
	static ACCESS_MASK getFileAccessForUser(std::wstring path, unsigned long rid);
	static std::vector<std::wstring> enumSubKeys(std::wstring key);
	static bool keyExists(std::wstring key);
	static bool valueExists(std::wstring key, std::wstring valuename);
	static bool keyEmpty(std::wstring key);
	static void saveToFile(std::wstring key, std::vector<std::wstring> valuenames, std::wstring filepath);
	static std::wstring getGuidString(GUID guid);
	static bool isWindowsVersionAtLeast(unsigned major, unsigned minor);
	static HKEY openKey(const std::wstring& key, REGSAM samDesired);

private:
	static std::wstring splitKey(const std::wstring& key, HKEY* rootKey);

	static unsigned long windowsVersion;
};

class RegistryException
{
public:
	RegistryException(const std::wstring& message)
		: message(message)
	{
	}

	std::wstring getMessage()
	{
		return message;
	}

private:
	std::wstring message;
};
