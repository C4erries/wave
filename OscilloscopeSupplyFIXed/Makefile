# =============================================================================
# OscilloscopeSupplyFIXed — сборка и запуск вне Visual Studio
#
# Требования для Windows (установить один раз):
#   1. .NET SDK 8+      — https://dot.net/download
#                         Нужен только для сборки (dotnet msbuild).
#   2. .NET Framework targeting pack 4.5.2
#                         Входит в Visual Studio Build Tools или устанавливается
#                         отдельно через Visual Studio Installer → «Individual
#                         components» → «.NET Framework 4.5.2 targeting pack».
#                         Либо скачать «Developer Pack» напрямую:
#                         https://dotnet.microsoft.com/download/dotnet-framework/net452
#   3. PicoScope SDK    — установить PicoScope 6 / SDK для ps5000a:
#                         https://www.picotech.com/downloads
#                         После установки ps5000a.dll появится в PATH автоматически.
#   4. make (GNU Make)  — входит в Git for Windows (Git Bash) или MSYS2.
#                         Запускать из Git Bash: make build / make run
#
# Для запуска готового .exe (без пересборки) достаточно только пунктов 1 и 3.
# .NET Framework 4.5.2 runtime встроен в Windows 8.1 / 10 / 11.
# =============================================================================

SLN      := ps5000a/Oscillocope_Supply.sln
CONFIG   ?= Release
PLATFORM ?= x64
EXE      := ps5000a/PS5000ABlockCapture/bin/$(PLATFORM)/$(CONFIG)/PS5000ABlockCapture.exe

.PHONY: build run clean

## Собрать проект
build:
	dotnet msbuild $(SLN) \
		/p:Configuration=$(CONFIG) \
		/p:Platform=$(PLATFORM) \
		/nologo /v:minimal

## Собрать и запустить
run: build
	./$(EXE)

## Удалить артефакты сборки
clean:
	dotnet msbuild $(SLN) /t:Clean \
		/p:Configuration=$(CONFIG) \
		/p:Platform=$(PLATFORM) \
		/nologo /v:minimal
