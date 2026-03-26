# VolvoTools

Tools for flashing and logging ME7 and ME9 memory with J2534 devices.
Initially written by dream3R from http://nefariousmotorsports.com/forum
for Hilton's Tuning Studio. 
HTS was decompiled and rewritten in C++.
Later was added support of generic J2534 devices not only DiCE
and support of UDS protocol.

Now supports
- ME7 flashing and logging for P80 and P2
- ME9 flashing and logging for P1
- ME9 and other UDS protocols flashing and logging
- Denso ECM flashing and logging for Volvo P3 platform.

Big thanks to rkam for the information and shared data.
Big thanks to jazdw for TP20 protocol explanation.
Big thanks to maZer.GTi for motivation.

## VolvoFlasher CLI profile (ECU/CEM)

`VolvoFlasher` has a Volvo-themed startup header and now supports selecting target module type:

- `--module ECU` (default)
- `--module CEM`

Example for P3 CEM workstream:

```bash
VolvoFlasher -f P3 -m CEM -e 0x42 flash -i your_file.bin
```

Example for classic ECU flashing on P2:

```bash
VolvoFlasher -f P2 -m ECU -e 0x7A flash -i your_file.bin
```


## Installer EXE (single package)

The CI pipeline now generates one Windows installer artifact:

- `VolvoTools_Installer.exe`

Installer is produced with CPack/NSIS and published as workflow artifact.
For tag builds, the same single installer is attached to the GitHub Release.

After installation you can edit GUI defaults in `VolvoToolsGui.config.json`
(placed next to `VolvoToolsGui.exe`) to change default module/platform/baudrate/PIN fields.

## Сборка проекта
### Требуемые приложения
Для работы с системой контроля версий нужно установить `git`. 
Также необходимо установить менеджер пакетов Conan и сборочную систему CMake.
В качестве IDE и компилятора можно поставить Visual Studio 2022 Community Edition.

### Процесс сборки
После установки `conan` нужно определить профили. Сделать это можно с помощью команды
`conan profile detect`

Выполнить клонирование проекта через git clone
`git clone git@github.com:prometey1982/VolvoTools`

Перейти в папку с проектом
`cd VolvoTools`

В папке выполнить комманду загрузки подмодулей
`git submodule update --init`

Далее выполнить

`conan install . --build=missing`

`cmake --preset conan-default`

После выполнения этих шагов, если все прошло успешно, будет создана папка `build` в которой будет создан файл `VolvoTools.sln` и сгенерирована вся необходимая информация для сборки. 

Далее, собрать проект можно с помощью следующей команды

`cmake --build build --config Release`

Также можно открыть файл `VolvoTools.sln` и собрать с помощью Visual Studio.

## Особенности работы с устройствами J2534

В попытках уменьшить количество открытий и закрытий КАН каналов, решил долговременно хранить их в сущности под названием J2534Info.
Но наткнулся на то, что канал открытый в одном потоке вызывает аварийное отключение устройства при попытке использовать в другом.
Данное поведение проявилось на адаптере MongoosePro JLR. Возможно, с другими устройствами ситуация аналогичная. В итоге, создал сущность J2534ChannelProvider.
С помощью этой сущности можно открывать все каналы, по какой-либо поддерживаемой платформе. А также открывать один канал, если нужно обратиться к конкретному ЭБУ.
Первая возможность используется в флешерах, чтобы увести устройства во всех сетях в сон. Вторая для работы с шиной конкретного ЭБУ, например, в логгерах.


## Тут нужно описать тонкости VAG TP20 с которыми столкнулся в рамках его реализации
