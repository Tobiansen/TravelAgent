#pragma once
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <cstdarg>
inline uint32_t testNow=100;
inline uint32_t millis(){return testNow;}
inline uint32_t micros(){return testNow*1000;}
struct TestSerial {
    std::vector<std::string> lines;
    void printf(const char* format,...){
        char bytes[2048];va_list args;va_start(args,format);
        vsnprintf(bytes,sizeof(bytes),format,args);va_end(args);lines.emplace_back(bytes);
    }
};
inline TestSerial Serial;
