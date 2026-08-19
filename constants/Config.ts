import { Platform } from 'react-native';

/**
 * SmartSchool API Configuration
 */

// Port used by the ASP.NET Core backend (from launchSettings.json)
const API_PORT = '5197';

// Determine host based on platform
// 10.0.2.2 is the special alias for your host loopback interface in Android Emulator
const getBaseHost = () => {
  if (Platform.OS === 'android') {
    return '10.0.2.2';
  }
  return 'localhost';
};

const BASE_URL = `http://${getBaseHost()}:${API_PORT}`;

export const Config = {
  API_BASE_URL: BASE_URL,
  ENDPOINTS: {
    LOGIN: `${BASE_URL}/api/auth/login`,
    STUDENTS: `${BASE_URL}/api/students`,
  },
  TIMEOUT: 10000, // 10 seconds
};
