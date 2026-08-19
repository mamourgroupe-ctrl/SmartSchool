import React, { useState } from 'react';
import { StyleSheet, Text, TextInput, TouchableOpacity, View, Alert, ActivityIndicator } from 'react-native';
import { useRouter } from 'expo-router';
import { Colors } from '@/constants/theme';
import { useColorScheme } from '@/hooks/use-color-scheme';
import { Config } from '@/constants/Config';

export default function LoginScreen() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const router = useRouter();
  const colorScheme = useColorScheme() ?? 'light';
  const colors = Colors[colorScheme];

  const handleLogin = async () => {
    if (!username || !password) {
      Alert.alert("خطأ", "يرجى إدخال اسم المستخدم وكلمة المرور");
      return;
    }

    setIsLoading(true);
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), Config.TIMEOUT);

      const response = await fetch(Config.ENDPOINTS.LOGIN, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
        signal: controller.signal,
      });

      clearTimeout(timeoutId);

      let data;
      const contentType = response.headers.get("content-type");
      if (contentType && contentType.indexOf("application/json") !== -1) {
        data = await response.json();
      } else {
        data = { message: await response.text() };
      }

      if (response.ok) {
        Alert.alert("نجاح", "تم تسجيل الدخول بنجاح!");
        router.replace('/(tabs)');
      } else {
        Alert.alert("خطأ", data.message || data.detail || "فشل تسجيل الدخول");
      }
    } catch (error: any) {
      console.error('[Login Error]', error);
      if (error.name === 'AbortError') {
        Alert.alert("انتهاء المهلة", "استغرق الخادم وقتاً طويلاً للرد.");
      } else {
        Alert.alert(
          "خطأ شبكة",
          `تعذر الاتصال بالخادم (${Config.API_BASE_URL}). تأكد من تشغيل الـ API ومن إعدادات الشبكة.`
        );
      }
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <View style={[styles.container, { backgroundColor: colors.background }]}>
      <Text style={[styles.title, { color: colors.text }]}>تسجيل دخول SmartSchool</Text>
      <TextInput
        style={[styles.input, { borderColor: colors.icon, color: colors.text }]}
        placeholder="اسم المستخدم"
        placeholderTextColor={colors.icon}
        value={username}
        onChangeText={setUsername}
        autoCapitalize="none"
      />
      <TextInput
        style={[styles.input, { borderColor: colors.icon, color: colors.text }]}
        placeholder="كلمة المرور"
        placeholderTextColor={colors.icon}
        secureTextEntry
        value={password}
        onChangeText={setPassword}
      />
      <TouchableOpacity
        style={[styles.button, { backgroundColor: colors.tint }]}
        onPress={handleLogin}
        disabled={isLoading}
      >
        {isLoading ? (
          <ActivityIndicator color="#fff" />
        ) : (
          <Text style={styles.buttonText}>دخول</Text>
        )}
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, justifyContent: 'center', padding: 20 },
  title: { fontSize: 24, fontWeight: 'bold', marginBottom: 30, textAlign: 'center' },
  input: { borderWidth: 1, padding: 12, borderRadius: 8, marginBottom: 15 },
  button: { padding: 15, borderRadius: 8, alignItems: 'center', height: 55, justifyContent: 'center' },
  buttonText: { color: '#fff', fontWeight: 'bold', fontSize: 16 }
});
