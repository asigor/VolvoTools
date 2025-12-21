#pragma once

#include <vector>
#include <string>

namespace common {

	enum class NetworkType {
		UNKNOWN,
		CAN_HS,
		CAN_MS
	};

	enum class FrameFormat {
		UNKNOWN,
		CAN_STANDARD,
		CAN_EXTENDED
	};

	enum class SWPartType {
		UNKNOWN,
		SBL,
		DATA,
		EXE,
		SIGCFG
	};

	struct EraseBlock
	{
		uint32_t startAddr;
		uint32_t length;
	};

	struct VBFHeader {
		double vbfVersion{};
		std::vector<std::string> description;
		std::string swPartNumber;
		std::string swVersion;
		SWPartType swPartType{ SWPartType::UNKNOWN };
		NetworkType network{ NetworkType::UNKNOWN };
		uint32_t ecuAddress{};
		FrameFormat frameFormat{ FrameFormat::UNKNOWN };
		uint32_t call{};
		uint32_t fileChecksum{};
		std::vector<EraseBlock> eraseBlocks;
	};

} // namespace common
