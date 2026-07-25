#pragma once

#include "FlasherParametersProviderBase.hpp"

#include <j2534/J2534.hpp>

#include <memory>

namespace flasher {

class FlasherBase;

class FlasherFactory {
public:
    static std::unique_ptr<FlasherBase> create(
        j2534::J2534& j2534,
        const FlasherParametersProviderBase& params);

    static bool isD2Platform(common::CarPlatform p);
    static bool isUDSPlatform(common::CarPlatform p);
};

} // namespace flasher
