import React, { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  RefreshControl,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { useRouter } from 'expo-router';
import { Colors } from '@/constants/theme';
import { useColorScheme } from '@/hooks/use-color-scheme';
import { Config, TOKEN_KEY, REFRESH_TOKEN_KEY } from '@/constants/Config';

type Student = {
  studentId: number;
  firstName: string;
  lastName: string;
};

// Try to exchange the stored refresh token for a new token pair (rotation).
async function tryRefresh(): Promise<string | null> {
  try {
    const refreshToken = await SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
    if (!refreshToken) return null;
    const response = await fetch(Config.ENDPOINTS.REFRESH, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    });
    if (!response.ok) return null;
    const data = await response.json();
    if (!data.accessToken) return null;
    await SecureStore.setItemAsync(TOKEN_KEY, data.accessToken);
    if (data.refreshToken) {
      await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, data.refreshToken);
    }
    return data.accessToken as string;
  } catch {
    return null;
  }
}

async function clearSession(): Promise<void> {
  await SecureStore.deleteItemAsync(TOKEN_KEY);
  await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
}

export default function HomeScreen() {
  const colorScheme = useColorScheme() ?? 'light';
  const colors = Colors[colorScheme];
  const router = useRouter();
  const [students, setStudents] = useState<Student[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadStudents = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const token = await SecureStore.getItemAsync(TOKEN_KEY);
      let response = await fetch(Config.ENDPOINTS.STUDENTS, {
        headers: { Authorization: `Bearer ${token ?? ''}` },
      });

      // Access token expired or revoked: try one silent refresh, then retry.
      if (response.status === 401) {
        const newToken = await tryRefresh();
        if (newToken) {
          response = await fetch(Config.ENDPOINTS.STUDENTS, {
            headers: { Authorization: `Bearer ${newToken}` },
          });
        } else {
          await clearSession();
          router.replace('/login');
          return;
        }
      }

      if (response.ok) {
        setStudents(await response.json());
      } else {
        setError('تعذر تحميل قائمة الطلاب');
      }
    } catch {
      setError(`تعذر الاتصال بالخادم (${Config.API_BASE_URL})`);
    } finally {
      setLoading(false);
    }
  }, [router]);

  useEffect(() => {
    loadStudents();
  }, [loadStudents]);

  const logout = async () => {
    // Best effort: revoke refresh tokens server-side before clearing locally.
    try {
      const token = await SecureStore.getItemAsync(TOKEN_KEY);
      if (token) {
        await fetch(Config.ENDPOINTS.LOGOUT, {
          method: 'POST',
          headers: { Authorization: `Bearer ${token}` },
        });
      }
    } catch {
      // Ignore network errors on logout; local session is cleared anyway.
    }
    await clearSession();
    router.replace('/login');
  };

  return (
    <View style={[styles.container, { backgroundColor: colors.background }]}>
      <Text style={[styles.title, { color: colors.text }]}>قائمة الطلاب</Text>
      {loading && students.length === 0 ? (
        <ActivityIndicator style={styles.center} color={colors.tint} />
      ) : error ? (
        <Text style={[styles.error, { color: '#c00' }]}>{error}</Text>
      ) : (
        <FlatList
          style={styles.list}
          data={students}
          keyExtractor={(item) => String(item.studentId)}
          refreshControl={<RefreshControl refreshing={loading} onRefresh={loadStudents} />}
          ListEmptyComponent={
            <Text style={[styles.empty, { color: colors.icon }]}>لا يوجد طلاب متاحون</Text>
          }
          renderItem={({ item }) => (
            <View style={[styles.card, { borderColor: colors.icon }]}>
              <Text style={[styles.name, { color: colors.text }]}>
                {item.firstName} {item.lastName}
              </Text>
            </View>
          )}
        />
      )}
      <TouchableOpacity
        style={[styles.logoutButton, { backgroundColor: colors.tint }]}
        onPress={logout}
      >
        <Text style={styles.logoutText}>تسجيل الخروج</Text>
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 20, paddingTop: 60 },
  title: { fontSize: 24, fontWeight: 'bold', marginBottom: 16, textAlign: 'center' },
  center: { marginTop: 40 },
  list: { flex: 1 },
  card: {
    borderWidth: 1,
    borderRadius: 8,
    padding: 14,
    marginBottom: 10,
    backgroundColor: '#ffffff22',
  },
  name: { fontSize: 16, fontWeight: '600' },
  empty: { textAlign: 'center', marginTop: 40, fontSize: 15 },
  error: { textAlign: 'center', marginTop: 40, fontSize: 15 },
  logoutButton: {
    padding: 15,
    borderRadius: 8,
    alignItems: 'center',
    marginTop: 12,
  },
  logoutText: { color: '#fff', fontWeight: 'bold', fontSize: 16 },
});
