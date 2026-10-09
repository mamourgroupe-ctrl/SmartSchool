import React from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { Colors } from '@/constants/theme';
import { useColorScheme } from '@/hooks/use-color-scheme';

export default function AboutScreen() {
  const colorScheme = useColorScheme() ?? 'light';
  const colors = Colors[colorScheme];

  return (
    <View style={[styles.container, { backgroundColor: colors.background }]}>
      <Text style={[styles.title, { color: colors.text }]}>SmartSchool</Text>
      <Text style={[styles.text, { color: colors.text }]}>
        منصة إدارة المدرسة ومتابعة حفظ القرآن الكريم
      </Text>
      <Text style={[styles.version, { color: colors.icon }]}>الإصدار 1.0.0</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  title: { fontSize: 28, fontWeight: 'bold', marginBottom: 12 },
  text: { fontSize: 16, textAlign: 'center', lineHeight: 24 },
  version: { marginTop: 24, fontSize: 14 },
});
